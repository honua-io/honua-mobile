using System.Net;
using System.Net.Http.Headers;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Field.Projects;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection.Services.Packages;

public interface IFieldProjectPackageDownloadRequestCustomizer
{
    ValueTask CustomizeAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}

public sealed class LocalFieldProjectPackageDownloadService
{
    private const string ManifestFileName = "field-project-package.json";

    private readonly HttpClient _httpClient;
    private readonly LocalFieldProjectPackageImportService _importService;
    private readonly IReadOnlyList<IFieldProjectPackageDownloadRequestCustomizer> _requestCustomizers;
    private readonly ILogger<LocalFieldProjectPackageDownloadService>? _logger;

    public LocalFieldProjectPackageDownloadService(
        HttpClient httpClient,
        LocalFieldProjectPackageImportService importService,
        IEnumerable<IFieldProjectPackageDownloadRequestCustomizer>? requestCustomizers = null,
        ILogger<LocalFieldProjectPackageDownloadService>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _requestCustomizers = requestCustomizers?.ToList() ?? [];
        _logger = logger;
    }

    public async Task<LocalFieldProjectPackageDownloadResult> DownloadAndImportAsync(
        LocalFieldProjectPackageDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ManifestUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DownloadRootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRootDirectory);

        if (!request.ManifestUri.IsAbsoluteUri)
        {
            return Failed(null, Error("invalid-manifest-uri", "$.manifestUri", "Package manifest URI must be absolute."));
        }

        var downloadRoot = Path.GetFullPath(request.DownloadRootDirectory);
        var partialDirectory = Path.Combine(downloadRoot, ".partial", Guid.NewGuid().ToString("N"));
        var manifestPath = Path.Combine(partialDirectory, ManifestFileName);
        Directory.CreateDirectory(partialDirectory);

        FieldProjectPackage? package = null;
        var downloadedFiles = new List<LocalFieldProjectPackageDownloadedFile>();
        var diagnostics = new List<LocalFieldProjectPackageDiagnostic>();
        var downloadedBytes = 0L;

        try
        {
            var manifestBytes = await DownloadFileAsync(
                request.ManifestUri,
                manifestPath,
                "$.manifestUri",
                "Package manifest",
                cancellationToken).ConfigureAwait(false);
            downloadedBytes += manifestBytes;

            try
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                package = FieldProjectPackage.ParseJson(manifestJson);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
            {
                _logger?.LogWarning(ex, "Failed to parse downloaded field project package manifest {ManifestUri}", request.ManifestUri);
                diagnostics.Add(Error("invalid-manifest", "$", ex.Message));
                return Failed(null, diagnostics, downloadedBytes: downloadedBytes);
            }

            var artifactBaseUri = request.ArtifactBaseUri is null
                ? ResolveManifestDirectoryUri(request.ManifestUri)
                : EnsureDirectoryUri(request.ArtifactBaseUri);
            for (var i = 0; i < package.OfflinePackages.Count; i++)
            {
                var offlinePackage = package.OfflinePackages[i];
                if (!TryNormalizeRelativePath(offlinePackage.RelativePath, out var relativePath))
                {
                    diagnostics.Add(Error(
                        "unsafe-offline-artifact-path",
                        $"$.offlinePackages[{i}].relativePath",
                        $"Offline package '{offlinePackage.PackageId}' has an unsafe relative path."));
                    continue;
                }

                var artifactUri = new Uri(artifactBaseUri, relativePath);
                var localPath = ResolvePackageFilePath(partialDirectory, relativePath);
                var artifactBytes = await DownloadFileAsync(
                    artifactUri,
                    localPath,
                    $"$.offlinePackages[{i}].relativePath",
                    $"Offline package '{offlinePackage.PackageId}'",
                    cancellationToken).ConfigureAwait(false);

                downloadedBytes += artifactBytes;
                downloadedFiles.Add(new LocalFieldProjectPackageDownloadedFile
                {
                    PackageId = offlinePackage.PackageId,
                    SourceUri = artifactUri,
                    RelativePath = relativePath,
                    LocalPath = localPath,
                    SizeBytes = artifactBytes
                });
            }

            if (diagnostics.Any(IsError))
            {
                return Failed(package.ProjectId, diagnostics, downloadedBytes: downloadedBytes, downloadedFiles: downloadedFiles);
            }

            var downloadDirectory = BuildDownloadDirectory(downloadRoot, package);
            if (Directory.Exists(downloadDirectory))
            {
                if (!request.OverwriteExisting)
                {
                    diagnostics.Add(Error(
                        "download-already-exists",
                        "$.projectId",
                        $"Project '{package.ProjectId}' is already downloaded at the destination."));
                    return Failed(package.ProjectId, diagnostics, downloadedBytes: downloadedBytes, downloadedFiles: downloadedFiles);
                }

                Directory.Delete(downloadDirectory, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(downloadDirectory)!);
            Directory.Move(partialDirectory, downloadDirectory);
            partialDirectory = string.Empty;

            var downloadedManifestPath = Path.Combine(downloadDirectory, ManifestFileName);
            var importResult = await _importService.ImportAsync(new LocalFieldProjectPackageImportRequest
            {
                ManifestPath = downloadedManifestPath,
                DestinationRootDirectory = request.DestinationRootDirectory,
                ImportSource = request.ManifestUri.AbsoluteUri,
                OverwriteExisting = request.OverwriteExisting
            }, cancellationToken).ConfigureAwait(false);

            diagnostics.AddRange(importResult.Diagnostics);

            return new LocalFieldProjectPackageDownloadResult
            {
                ProjectId = package.ProjectId,
                Downloaded = true,
                DownloadedManifestPath = downloadedManifestPath,
                DownloadDirectory = downloadDirectory,
                ImportResult = importResult,
                Diagnostics = diagnostics,
                DownloadedFiles = downloadedFiles,
                DownloadedBytes = downloadedBytes
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to download field project package {ManifestUri}", request.ManifestUri);
            diagnostics.Add(ToDownloadDiagnostic(ex));
            return Failed(package?.ProjectId, diagnostics, downloadedBytes: downloadedBytes, downloadedFiles: downloadedFiles);
        }
        finally
        {
            if (request.CleanupPartialDownloadOnFailure && !string.IsNullOrWhiteSpace(partialDirectory))
            {
                DeleteDirectoryIfExists(partialDirectory);
            }
        }
    }

    private async Task<long> DownloadFileAsync(
        Uri uri,
        string destinationPath,
        string diagnosticPath,
        string description,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partialPath = destinationPath + ".partial";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var customizer in _requestCustomizers)
        {
            await customizer.CustomizeAsync(request, cancellationToken).ConfigureAwait(false);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new FieldProjectPackageDownloadException(
                "download-http-error",
                diagnosticPath,
                $"{description} download failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase ?? response.StatusCode.ToString()}.");
        }

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(partialPath, destinationPath);
        return new FileInfo(destinationPath).Length;
    }

    private static Uri EnsureDirectoryUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            throw new FieldProjectPackageDownloadException(
                "invalid-artifact-base-uri",
                "$.artifactBaseUri",
                "Package artifact base URI must be absolute.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
    }

    private static Uri ResolveManifestDirectoryUri(Uri manifestUri)
    {
        if (!manifestUri.IsAbsoluteUri)
        {
            throw new FieldProjectPackageDownloadException(
                "invalid-manifest-uri",
                "$.manifestUri",
                "Package manifest URI must be absolute.");
        }

        return manifestUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? manifestUri
            : new Uri(manifestUri.AbsoluteUri[..(manifestUri.AbsoluteUri.LastIndexOf('/') + 1)]);
    }

    private static bool TryNormalizeRelativePath(string? relativePath, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Contains('\\', StringComparison.Ordinal) ||
            relativePath.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _))
        {
            return false;
        }

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    private static string ResolvePackageFilePath(string packageDirectory, string relativePath)
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
            throw new FieldProjectPackageDownloadException(
                "unsafe-offline-artifact-path",
                "$.offlinePackages",
                "Resolved offline artifact path is outside the package directory.");
        }

        return fullPath;
    }

    private static string BuildDownloadDirectory(
        string downloadRootDirectory,
        FieldProjectPackage package)
    {
        var version = string.IsNullOrWhiteSpace(package.Version) ? "current" : package.Version;
        return Path.Combine(
            downloadRootDirectory,
            SanitizePathSegment(package.ProjectId),
            SanitizePathSegment(version));
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray()).Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "package" : sanitized;
    }

    private static LocalFieldProjectPackageDownloadResult Failed(
        string? projectId,
        IEnumerable<LocalFieldProjectPackageDiagnostic> diagnostics,
        IReadOnlyList<LocalFieldProjectPackageDownloadedFile>? downloadedFiles = null,
        long downloadedBytes = 0)
        => new()
        {
            ProjectId = projectId,
            Downloaded = false,
            Diagnostics = diagnostics.ToList(),
            DownloadedFiles = downloadedFiles ?? [],
            DownloadedBytes = downloadedBytes
        };

    private static LocalFieldProjectPackageDownloadResult Failed(
        string? projectId,
        params LocalFieldProjectPackageDiagnostic[] diagnostics)
        => Failed(projectId, diagnostics.AsEnumerable());

    private static LocalFieldProjectPackageDiagnostic ToDownloadDiagnostic(Exception exception)
        => exception is FieldProjectPackageDownloadException downloadException
            ? Error(downloadException.Code, downloadException.Path, downloadException.Message)
            : Error("download-failed", "$.manifestUri", exception.Message);

    private static LocalFieldProjectPackageDiagnostic Error(string code, string path, string message)
        => new()
        {
            Code = code,
            Path = path,
            Message = message,
            Severity = LocalFieldProjectPackageDiagnosticSeverity.Error
        };

    private static bool IsError(LocalFieldProjectPackageDiagnostic diagnostic)
        => diagnostic.Severity == LocalFieldProjectPackageDiagnosticSeverity.Error;

    private static void DeleteDirectoryIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FieldProjectPackageDownloadException : HttpRequestException
    {
        public FieldProjectPackageDownloadException(string code, string path, string message)
            : base(message)
        {
            Code = code;
            Path = path;
        }

        public string Code { get; }

        public string Path { get; }
    }
}

public sealed class FieldProjectPackageDownloadAuthHeader : IFieldProjectPackageDownloadRequestCustomizer
{
    private readonly IAuthenticationService _authService;

    public FieldProjectPackageDownloadAuthHeader(IAuthenticationService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async ValueTask CustomizeAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ShouldAttachCredentials(request.RequestUri))
        {
            return;
        }

        await _authService.EnsureValidSessionAsync(cancellationToken).ConfigureAwait(false);
        var token = await _authService.GetAuthTokenAsync(cancellationToken).ConfigureAwait(false);
        switch (token?.Scheme)
        {
            case HonuaAuthScheme.ApiKey when !string.IsNullOrWhiteSpace(token.AccessToken):
                request.Headers.TryAddWithoutValidation("X-API-Key", token.AccessToken);
                break;
            case HonuaAuthScheme.Bearer when !string.IsNullOrWhiteSpace(token.AccessToken):
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                break;
        }
    }

    private bool ShouldAttachCredentials(Uri? endpoint)
    {
        return endpoint is not null &&
            Uri.TryCreate(_authService.ServerUrl, UriKind.Absolute, out var serverUri) &&
            Uri.Compare(
                endpoint,
                serverUri,
                UriComponents.SchemeAndServer,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0;
    }
}
