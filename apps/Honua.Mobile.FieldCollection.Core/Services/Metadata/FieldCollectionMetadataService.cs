using System.Net.Http.Headers;
using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.GeoServices.FeatureServer.Models;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection.Services.Metadata;

public sealed class FieldCollectionMetadataService : IFieldCollectionMetadataService
{
    public const string DefaultServiceId = "mobile_offline_demo";

    private const string SelectedServiceIdKey = "field_collection.selected_service_id";

    private readonly IAuthenticationService _authenticationService;
    private readonly ISettingsService _settingsService;
    private readonly GeoPackageStorageService _storageService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FieldCollectionMetadataService>? _logger;

    public FieldCollectionMetadataService(
        IAuthenticationService authenticationService,
        ISettingsService settingsService,
        GeoPackageStorageService storageService,
        HttpClient httpClient,
        ILogger<FieldCollectionMetadataService>? logger = null)
    {
        _authenticationService = authenticationService;
        _settingsService = settingsService;
        _storageService = storageService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FieldProjectInfo>> GetProjectsAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var cachedProjects = await GetCachedProjectsAsync(cancellationToken).ConfigureAwait(false);
        if (!refresh && cachedProjects.Count > 0)
        {
            return cachedProjects;
        }

        if (CanLoadRemoteMetadata())
        {
            try
            {
                var remoteProjects = await LoadRemoteProjectsAsync(cancellationToken).ConfigureAwait(false);
                if (remoteProjects.Count > 0)
                {
                    return MergeProjectOfflineState(remoteProjects, cachedProjects);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Failed to load field collection project metadata from Honua server");
            }
        }

        if (cachedProjects.Count > 0)
        {
            return cachedProjects;
        }

        var selectedServiceId = await GetSelectedServiceIdAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            new FieldProjectInfo
            {
                ServiceId = selectedServiceId,
                Name = selectedServiceId,
                Description = "Configured Honua feature service",
                IsAvailableOffline = false
            }
        ];
    }

    public async Task<FieldProjectInfo?> GetSelectedProjectAsync(CancellationToken cancellationToken = default)
    {
        var selectedServiceId = await GetSelectedServiceIdAsync(cancellationToken).ConfigureAwait(false);
        var projects = await GetProjectsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return projects.FirstOrDefault(project => string.Equals(project.ServiceId, selectedServiceId, StringComparison.OrdinalIgnoreCase)) ??
            projects.FirstOrDefault();
    }

    public async Task SelectProjectAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            throw new ArgumentException("Service ID is required.", nameof(serviceId));
        }

        await _settingsService.SetSettingAsync(SelectedServiceIdKey, serviceId.Trim()).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LayerInfo>> GetLayersAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var selectedServiceId = await GetSelectedServiceIdAsync(cancellationToken).ConfigureAwait(false);
        var cachedLayers = await GetCachedLayersAsync(selectedServiceId, cancellationToken).ConfigureAwait(false);

        if ((refresh || cachedLayers.Count == 0) && CanLoadRemoteMetadata())
        {
            try
            {
                var remoteLayers = await LoadRemoteLayersAsync(selectedServiceId, cancellationToken).ConfigureAwait(false);
                foreach (var layer in remoteLayers)
                {
                    await _storageService.CreateLayerAsync(layer).ConfigureAwait(false);
                }

                return remoteLayers;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Failed to load layer metadata for service {ServiceId}", selectedServiceId);
            }
        }

        return cachedLayers;
    }

    private async Task<IReadOnlyList<FieldProjectInfo>> GetCachedProjectsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var layers = await _storageService.GetLayersAsync().ConfigureAwait(false);
        return layers
            .GroupBy(layer => string.IsNullOrWhiteSpace(layer.ServiceId) ? DefaultServiceId : layer.ServiceId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var projectLayers = group.ToList();
                return new FieldProjectInfo
                {
                    ServiceId = group.Key,
                    Name = group.Key,
                    Description = "Cached Honua feature service",
                    LayerCount = projectLayers.Count,
                    IsAvailableOffline = true,
                    Layers = projectLayers
                };
            })
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<LayerInfo>> GetCachedLayersAsync(string selectedServiceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var layers = await _storageService.GetLayersAsync().ConfigureAwait(false);
        var matchingLayers = layers
            .Where(layer => string.Equals(layer.ServiceId, selectedServiceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingLayers.Count > 0)
        {
            return matchingLayers;
        }

        return layers;
    }

    private async Task<IReadOnlyList<FieldProjectInfo>> LoadRemoteProjectsAsync(CancellationToken cancellationToken)
    {
        var uri = BuildUri("/rest/services?f=json");
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("services", out var services) ||
            services.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var projects = new List<FieldProjectInfo>();
        foreach (var service in services.EnumerateArray())
        {
            var serviceId = ReadString(service, "name");
            if (string.IsNullOrWhiteSpace(serviceId) || !IsFeatureServerService(service))
            {
                continue;
            }

            projects.Add(new FieldProjectInfo
            {
                ServiceId = serviceId,
                Name = serviceId,
                Description = ReadString(service, "description") ?? "Honua feature service",
                IsAvailableOffline = false
            });
        }

        return projects
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<LayerInfo>> LoadRemoteLayersAsync(
        string serviceId,
        CancellationToken cancellationToken)
    {
        var serviceInfo = await GetJsonAsync<FeatureServerServiceInfo>(
            $"/rest/services/{Uri.EscapeDataString(serviceId)}/FeatureServer?f=json",
            cancellationToken).ConfigureAwait(false);

        var layerSummaries = serviceInfo.Layers ?? [];
        var layers = new List<LayerInfo>(layerSummaries.Count);
        foreach (var layerSummary in layerSummaries)
        {
            var layerInfo = await GetJsonAsync<FeatureServerLayerInfo>(
                $"/rest/services/{Uri.EscapeDataString(serviceId)}/FeatureServer/{layerSummary.Id}?f=json",
                cancellationToken).ConfigureAwait(false);

            layers.Add(FieldCollectionMetadataMapper.ToLayerInfo(serviceId, layerInfo));
        }

        return layers;
    }

    private async Task<T> GetJsonAsync<T>(string pathAndQuery, CancellationToken cancellationToken)
    {
        var uri = BuildUri(pathAndQuery);
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ??
            throw new JsonException($"Failed to deserialize {typeof(T).Name}.");
    }

    private async Task<string> GetSelectedServiceIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectedServiceId = await _settingsService.GetSettingAsync(SelectedServiceIdKey, DefaultServiceId).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(selectedServiceId) ? DefaultServiceId : selectedServiceId.Trim();
    }

    private bool CanLoadRemoteMetadata()
    {
        return _authenticationService.IsAuthenticated &&
            !string.IsNullOrWhiteSpace(_authenticationService.ServerUrl);
    }

    private Uri BuildUri(string pathAndQuery)
    {
        if (!Uri.TryCreate(_authenticationService.ServerUrl, UriKind.Absolute, out var serverUri))
        {
            throw new InvalidOperationException("A valid Honua server URL is required to load field metadata.");
        }

        return new Uri(serverUri, pathAndQuery);
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_authenticationService.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _authenticationService.ApiKey);
        }

        return request;
    }

    private static IReadOnlyList<FieldProjectInfo> MergeProjectOfflineState(
        IReadOnlyList<FieldProjectInfo> remoteProjects,
        IReadOnlyList<FieldProjectInfo> cachedProjects)
    {
        var cachedByServiceId = cachedProjects.ToDictionary(
            project => project.ServiceId,
            StringComparer.OrdinalIgnoreCase);

        return remoteProjects
            .Select(project =>
            {
                if (!cachedByServiceId.TryGetValue(project.ServiceId, out var cachedProject))
                {
                    return project;
                }

                project.IsAvailableOffline = true;
                project.LayerCount = cachedProject.LayerCount;
                project.Layers = cachedProject.Layers;
                return project;
            })
            .ToList();
    }

    private static bool IsFeatureServerService(JsonElement service)
    {
        var type = ReadString(service, "type") ?? ReadString(service, "serviceType");
        return type is null ||
            type.Contains("FeatureServer", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("Feature Service", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
