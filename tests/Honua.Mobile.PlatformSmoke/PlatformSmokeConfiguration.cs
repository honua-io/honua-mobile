using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Honua.Mobile.PlatformSmoke;

internal sealed record PlatformSmokeConfiguration(
    Uri BaseUri,
    string ServiceId,
    int LayerId,
    string? ApiKey,
    string ResultDirectory)
{
    public static string DefaultResultDirectory
        => FirstWritableDirectory() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static PlatformSmokeConfiguration Load()
    {
        var fileConfig = ReadFileConfiguration();

        var baseUrl = ReadRequired("HONUA_MOBILE_SMOKE_BASE_URL", "baseUrl", fileConfig);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("HONUA_MOBILE_SMOKE_BASE_URL/baseUrl must be an absolute URI.");
        }

        var serviceId = ReadRequired("HONUA_MOBILE_SMOKE_SERVICE_ID", "serviceId", fileConfig);
        var layerIdValue = ReadRequired("HONUA_MOBILE_SMOKE_LAYER_ID", "layerId", fileConfig);
        if (!int.TryParse(layerIdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layerId))
        {
            throw new InvalidOperationException("HONUA_MOBILE_SMOKE_LAYER_ID/layerId must be an integer.");
        }

        return new PlatformSmokeConfiguration(
            EnsureDirectoryUri(baseUri),
            serviceId,
            layerId,
            ReadOptional("HONUA_MOBILE_SMOKE_API_KEY", "apiKey", fileConfig),
            fileConfig?.Directory ?? DefaultResultDirectory);
    }

    private static FileConfiguration? ReadFileConfiguration()
    {
        foreach (var directory in CandidateDirectories())
        {
            var path = Path.Combine(directory, PlatformSmokeRunner.ConfigFileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => null,
                };
            }

            return new FileConfiguration(directory, values);
        }

        return null;
    }

    private static string ReadRequired(string environmentName, string jsonName, FileConfiguration? fileConfiguration)
    {
        return ReadOptional(environmentName, jsonName, fileConfiguration)
            ?? throw new InvalidOperationException(
                $"Missing required platform smoke setting {environmentName} or {jsonName} in {PlatformSmokeRunner.ConfigFileName}.");
    }

    private static string? ReadOptional(string environmentName, string jsonName, FileConfiguration? fileConfiguration)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        if (fileConfiguration is not null &&
            fileConfiguration.Values.TryGetValue(jsonName, out var fileValue) &&
            !string.IsNullOrWhiteSpace(fileValue))
        {
            return fileValue.Trim();
        }

        return null;
    }

    private static Uri EnsureDirectoryUri(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && !uri.AbsolutePath.EndsWith('/'))
        {
            return new Uri(uri.AbsoluteUri + "/");
        }

        return uri;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        foreach (var directory in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            SafeAppDataDirectory(),
        })
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return directory;
            }
        }
    }

    private static string? FirstWritableDirectory()
    {
        foreach (var directory in CandidateDirectories())
        {
            try
            {
                Directory.CreateDirectory(directory);
                return directory;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string? SafeAppDataDirectory()
    {
        try
        {
            return FileSystem.AppDataDirectory;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotImplementedException)
        {
            return null;
        }
    }

    private sealed record FileConfiguration(string Directory, IReadOnlyDictionary<string, string?> Values);
}
