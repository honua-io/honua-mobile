using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Sdk.Scenes;

namespace Honua.Mobile.Offline.ScenePackages;

/// <summary>
/// HTTP downloader for immutable offline 3D scene packages.
/// </summary>
public sealed class ScenePackageDownloader : IHonuaScenePackageDownloader
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IGeoPackageSyncStore _syncStore;

    /// <summary>
    /// Initializes a new <see cref="ScenePackageDownloader"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client used to fetch package-local assets.</param>
    /// <param name="syncStore">Offline store where package metadata is registered.</param>
    public ScenePackageDownloader(HttpClient httpClient, IGeoPackageSyncStore syncStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _syncStore = syncStore ?? throw new ArgumentNullException(nameof(syncStore));
    }

    /// <inheritdoc />
    public async Task<ScenePackageDownloadResult> DownloadAsync(
        ScenePackageDownloadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.AssetBaseUri);

        var utcNow = request.UtcNow ?? DateTimeOffset.UtcNow;
        ValidateRequest(request, utcNow);

        var outputDirectory = NormalizeOutputDirectory(request.OutputDirectory);
        var safePackageId = SanitizePackageId(request.Manifest.PackageId!);
        var stagingDirectory = Path.Combine(outputDirectory, $"{safePackageId}.partial");
        var readyDirectory = Path.Combine(outputDirectory, safePackageId);
        var baseUri = EnsureDirectoryUri(request.AssetBaseUri);
        var downloadedAssetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downloadedAssetCount = 0;
        var downloadedBytes = 0L;

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            foreach (var asset in request.Manifest.Assets)
            {
                ct.ThrowIfCancellationRequested();
                if (asset is null)
                {
                    throw new InvalidOperationException("Scene package manifest contains a null asset entry.");
                }

                try
                {
                    var assetBytes = await DownloadAssetAsync(
                        asset,
                        baseUri,
                        stagingDirectory,
                        request.Manifest.ByteBudget!.MaxPackageBytes!.Value,
                        downloadedBytes,
                        request.Progress,
                        downloadedAssetCount,
                        request.Manifest.Assets.Count,
                        request.Manifest.ByteBudget.DeclaredBytes,
                        ct).ConfigureAwait(false);

                    downloadedBytes += assetBytes;
                    downloadedAssetCount++;
                    downloadedAssetKeys.Add(asset.Key!);
                }
                catch (Exception ex) when (!asset.Required && ex is not OperationCanceledException)
                {
                    DeletePartialAsset(stagingDirectory, asset.Path);
                }
            }

            var finalValidation = request.Manifest.Validate(utcNow, downloadedAssetKeys);
            if (!finalValidation.IsValid)
            {
                throw new InvalidOperationException(
                    $"Scene package '{request.Manifest.PackageId}' did not pass final validation: {FormatIssues(finalValidation)}");
            }

            var manifestPath = await WriteManifestAsync(stagingDirectory, request.Manifest, ct).ConfigureAwait(false);
            ReplaceReadyDirectory(stagingDirectory, readyDirectory);
            manifestPath = Path.Combine(readyDirectory, "manifest.json");

            var state = request.Manifest.Validate(utcNow, downloadedAssetKeys).State;
            var storedBytes = SumDownloadedAssetBytes(request.Manifest, downloadedAssetKeys);
            var catalogRecord = BuildCatalogRecord(
                request.Manifest,
                readyDirectory,
                manifestPath,
                state,
                storedBytes,
                downloadedAssetCount,
                downloadedAssetKeys,
                utcNow);

            await _syncStore.InitializeAsync(ct).ConfigureAwait(false);
            await _syncStore.UpsertScenePackageAsync(catalogRecord, ct).ConfigureAwait(false);

            return new ScenePackageDownloadResult
            {
                PackageDirectory = readyDirectory,
                ManifestPath = manifestPath,
                DownloadedAssetCount = downloadedAssetCount,
                DownloadedBytes = downloadedBytes,
                CatalogRecord = catalogRecord,
            };
        }
        catch
        {
            if (request.CleanupPartialPackageOnFailure)
            {
                DeleteDirectoryIfExists(stagingDirectory);
            }

            throw;
        }
    }

    private static void ValidateRequest(ScenePackageDownloadRequest request, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new InvalidOperationException("Output directory is required.");
        }

        var validation = request.Manifest.Validate(utcNow);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Scene package manifest is invalid: {FormatIssues(validation)}");
        }

        if (request.Manifest.AuthExpiresAtUtc.HasValue && utcNow >= request.Manifest.AuthExpiresAtUtc.Value)
        {
            throw new InvalidOperationException(
                $"Scene package '{request.Manifest.PackageId}' download credentials have expired.");
        }

        if (string.IsNullOrWhiteSpace(request.Manifest.PackageId))
        {
            throw new InvalidOperationException("Scene package manifest is missing packageId.");
        }
    }

    private async Task<long> DownloadAssetAsync(
        HonuaScenePackageAsset asset,
        Uri baseUri,
        string stagingDirectory,
        long maxPackageBytes,
        long downloadedPackageBytes,
        IProgress<ScenePackageDownloadProgress>? progress,
        int completedAssetCount,
        int totalAssetCount,
        long? declaredBytes,
        CancellationToken ct)
    {
        var assetKey = asset.Key ?? throw new InvalidOperationException("Scene package asset is missing key.");
        var relativePath = RequireSafeRelativePath(asset.Path, assetKey);
        var assetUri = new Uri(baseUri, relativePath);
        var finalPath = BuildPackageFilePath(stagingDirectory, relativePath);
        var partialPath = finalPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        var expectedBytes = asset.Bytes ?? throw new InvalidOperationException($"Scene package asset '{assetKey}' is missing bytes.");
        var resumeBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;
        if (resumeBytes < 0 || resumeBytes > expectedBytes)
        {
            File.Delete(partialPath);
            resumeBytes = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, assetUri);
        if (resumeBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeBytes, null);
            if (!string.IsNullOrWhiteSpace(asset.ETag) && EntityTagHeaderValue.TryParse(asset.ETag, out var entityTag))
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(entityTag);
            }
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resumeBytes > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            File.Delete(partialPath);
            resumeBytes = 0;
        }
        else if (resumeBytes > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidOperationException(
                $"Scene package asset '{assetKey}' did not return a resumable HTTP 206 response.");
        }

        response.EnsureSuccessStatusCode();
        ValidateEtag(asset, response);

        if (response.Content.Headers.ContentLength is long contentLength &&
            resumeBytes + contentLength > expectedBytes)
        {
            throw new InvalidOperationException(
                $"Scene package asset '{assetKey}' exceeds declared bytes ({expectedBytes}).");
        }

        var bytesRead = await CopyAssetAsync(
            response.Content,
            partialPath,
            assetKey,
            resumeBytes,
            expectedBytes,
            maxPackageBytes,
            downloadedPackageBytes,
            progress,
            completedAssetCount,
            totalAssetCount,
            declaredBytes,
            ct).ConfigureAwait(false);

        var finalBytes = resumeBytes + bytesRead;
        if (finalBytes != expectedBytes)
        {
            throw new InvalidOperationException(
                $"Scene package asset '{assetKey}' downloaded {finalBytes} bytes but manifest declares {expectedBytes} bytes.");
        }

        await ValidateSha256Async(partialPath, asset, ct).ConfigureAwait(false);
        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        File.Move(partialPath, finalPath);
        return bytesRead;
    }

    private static async Task<long> CopyAssetAsync(
        HttpContent content,
        string partialPath,
        string assetKey,
        long resumeBytes,
        long expectedBytes,
        long maxPackageBytes,
        long downloadedPackageBytes,
        IProgress<ScenePackageDownloadProgress>? progress,
        int completedAssetCount,
        int totalAssetCount,
        long? declaredBytes,
        CancellationToken ct)
    {
        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var bytesRead = 0L;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
                var assetTotalBytes = resumeBytes + bytesRead;
                var packageTotalBytes = downloadedPackageBytes + bytesRead;

                if (assetTotalBytes > expectedBytes)
                {
                    throw new InvalidOperationException(
                        $"Scene package asset '{assetKey}' exceeds declared bytes ({expectedBytes}).");
                }

                if (packageTotalBytes > maxPackageBytes)
                {
                    throw new InvalidOperationException(
                        $"Scene package download exceeds package byte budget ({maxPackageBytes}).");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report(new ScenePackageDownloadProgress
                {
                    AssetKey = assetKey,
                    CompletedAssetCount = completedAssetCount,
                    TotalAssetCount = totalAssetCount,
                    DownloadedBytes = packageTotalBytes,
                    DeclaredBytes = declaredBytes,
                });
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return bytesRead;
    }

    private static async Task ValidateSha256Async(
        string partialPath,
        HonuaScenePackageAsset asset,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(partialPath);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        var expected = asset.Sha256 ?? throw new InvalidOperationException($"Scene package asset '{asset.Key}' is missing sha256.");
        if (HashMatches(expected, hash))
        {
            return;
        }

        throw new InvalidOperationException($"Scene package asset '{asset.Key}' failed SHA-256 validation.");
    }

    private static bool HashMatches(string expected, byte[] actual)
    {
        var normalized = expected.Trim();
        if (normalized.Length == 64 && normalized.All(Uri.IsHexDigit))
        {
            return string.Equals(
                normalized,
                Convert.ToHexString(actual).ToLowerInvariant(),
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(normalized, Convert.ToBase64String(actual), StringComparison.Ordinal);
    }

    private static void ValidateEtag(HonuaScenePackageAsset asset, HttpResponseMessage response)
    {
        if (string.IsNullOrWhiteSpace(asset.ETag))
        {
            return;
        }

        if (response.Headers.ETag is null)
        {
            throw new InvalidOperationException($"Scene package asset '{asset.Key}' response is missing the manifest ETag.");
        }

        if (!string.Equals(asset.ETag, response.Headers.ETag.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Scene package asset '{asset.Key}' ETag does not match the manifest.");
        }
    }

    private static async Task<string> WriteManifestAsync(
        string stagingDirectory,
        HonuaScenePackageManifest manifest,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(stagingDirectory, "manifest.json");
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, ManifestJsonOptions, ct).ConfigureAwait(false);
        return manifestPath;
    }

    private static ScenePackageRecord BuildCatalogRecord(
        HonuaScenePackageManifest manifest,
        string readyDirectory,
        string manifestPath,
        HonuaScenePackageState state,
        long downloadedBytes,
        int downloadedAssetCount,
        ISet<string> downloadedAssetKeys,
        DateTimeOffset utcNow)
        => new()
        {
            PackageId = manifest.PackageId!,
            SceneId = manifest.SceneId!,
            DisplayName = manifest.DisplayName,
            EditionGate = manifest.EditionGate!,
            ServerRevision = manifest.ServerRevision!,
            Extent = manifest.Extent,
            PackageDirectory = readyDirectory,
            ManifestPath = manifestPath,
            State = state,
            DeclaredBytes = manifest.ByteBudget?.DeclaredBytes ?? 0,
            DownloadedBytes = downloadedBytes,
            RequiredAssetCount = manifest.Assets.Count(asset => asset?.Required == true),
            DownloadedAssetCount = downloadedAssetCount,
            MissingOptionalAssetKeys = GetMissingOptionalAssetKeys(manifest, downloadedAssetKeys),
            StaleAfterUtc = manifest.StaleAfterUtc,
            OfflineUseExpiresAtUtc = manifest.OfflineUseExpiresAtUtc,
            AuthExpiresAtUtc = manifest.AuthExpiresAtUtc,
            UpdatedAtUtc = utcNow,
        };

    private static IReadOnlyList<string> GetMissingOptionalAssetKeys(
        HonuaScenePackageManifest manifest,
        ISet<string> downloadedAssetKeys)
        => manifest.Assets
            .Where(asset => asset is { Required: false, Key: not null } && !downloadedAssetKeys.Contains(asset.Key))
            .Select(asset => asset.Key!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static long SumDownloadedAssetBytes(
        HonuaScenePackageManifest manifest,
        ISet<string> downloadedAssetKeys)
    {
        var total = 0L;
        foreach (var asset in manifest.Assets)
        {
            if (asset?.Key is null || !downloadedAssetKeys.Contains(asset.Key))
            {
                continue;
            }

            checked
            {
                total += asset.Bytes ?? 0;
            }
        }

        return total;
    }

    private static string NormalizeOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Output directory is required.");
        }

        var normalized = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(normalized);
        return normalized;
    }

    private static string SanitizePackageId(string packageId)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(packageId.Length);

        foreach (var character in packageId.Trim())
        {
            if (character == Path.DirectorySeparatorChar ||
                character == Path.AltDirectorySeparatorChar ||
                character == ':' ||
                character == '.' ||
                Array.IndexOf(invalidCharacters, character) >= 0)
            {
                builder.Append('_');
                continue;
            }

            builder.Append(character);
        }

        var safePackageId = builder.ToString().Trim('.');
        if (string.IsNullOrWhiteSpace(safePackageId))
        {
            throw new InvalidOperationException("Package ID must include at least one valid file-name character.");
        }

        return safePackageId;
    }

    private static Uri EnsureDirectoryUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("Asset base URI must be absolute.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
    }

    private static string RequireSafeRelativePath(string? path, string assetKey)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"Scene package asset '{assetKey}' path must be package-local and relative.");
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException($"Scene package asset '{assetKey}' path must stay under the package root.");
        }

        return string.Join('/', segments);
    }

    private static string BuildPackageFilePath(string packageDirectory, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(packageDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(packageDirectory);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidOperationException("Resolved scene package asset path is outside the package directory.");
        }

        return fullPath;
    }

    private static void ReplaceReadyDirectory(string stagingDirectory, string readyDirectory)
    {
        DeleteDirectoryIfExists(readyDirectory);
        Directory.Move(stagingDirectory, readyDirectory);
    }

    private static void DeletePartialAsset(string stagingDirectory, string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        var partialPath = Path.Combine(stagingDirectory, assetPath.Replace('/', Path.DirectorySeparatorChar)) + ".partial";
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string FormatIssues(HonuaScenePackageValidationResult validation)
        => string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));
}
