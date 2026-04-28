using Honua.Mobile.Field.Forms;
using Honua.Mobile.Field.Records;
using Honua.Mobile.Maui.Annotations;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.MapAreas;
using Honua.Mobile.Offline.ScenePackages;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Routing;
using Honua.Mobile.Sdk.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui;

/// <summary>
/// Extension methods for registering Honua Mobile SDK services with the .NET MAUI dependency injection container.
/// </summary>
public static class HonuaMobileServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="HonuaMobileClient"/> and its options as singletons.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientOptions">Client configuration including endpoints and authentication.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="clientOptions"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddHonuaMobileSdk(
        this IServiceCollection services,
        HonuaMobileClientOptions clientOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientOptions);

        services.AddSingleton(clientOptions);
        services.AddHttpClient("HonuaMobile");
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient("HonuaMobile");
            return new HonuaMobileClient(httpClient, clientOptions);
        });
        return services;
    }

    /// <summary>
    /// Registers the default <see cref="IOfflineOperationUploader"/> implementation that uploads
    /// offline edits via the Honua API.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaApiOfflineUploader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOfflineOperationUploader, HonuaApiOfflineOperationUploader>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="HonuaRoutingClient"/> from the configured <see cref="HonuaMobileClient"/>.
    /// Requires <see cref="AddHonuaMobileSdk"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp => sp.GetRequiredService<HonuaMobileClient>().Routing);
        return services;
    }

    /// <summary>
    /// Registers <see cref="IHonuaSceneService"/> from the configured <see cref="HonuaMobileClient"/>.
    /// Requires <see cref="AddHonuaMobileSdk"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaScenes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHonuaSceneService>(sp => sp.GetRequiredService<HonuaMobileClient>().Scenes);
        return services;
    }

    /// <summary>
    /// Registers field data-collection services: <see cref="FormValidator"/>,
    /// <see cref="CalculatedFieldEvaluator"/>, <see cref="RecordWorkflow"/>, and <see cref="DuplicateDetector"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaMobileFieldCollection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<FormValidator>();
        services.AddSingleton<CalculatedFieldEvaluator>();
        services.AddSingleton<RecordWorkflow>();
        services.AddSingleton<DuplicateDetector>();
        return services;
    }

    /// <summary>
    /// Registers the GeoPackage-based offline sync stack: <see cref="IGeoPackageSyncStore"/>,
    /// <see cref="OfflineSyncEngine"/>, and <see cref="IOfflineSyncRunner"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="storeOptions">GeoPackage store configuration (database path, etc.).</param>
    /// <param name="syncOptions">Sync engine options; defaults are used when <see langword="null"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="storeOptions"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddHonuaGeoPackageOfflineSync(
        this IServiceCollection services,
        GeoPackageSyncStoreOptions storeOptions,
        OfflineSyncEngineOptions? syncOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(storeOptions);

        services.AddSingleton(storeOptions);
        services.AddSingleton<IGeoPackageSyncStore, GeoPackageSyncStore>();
        services.AddSingleton(syncOptions ?? new OfflineSyncEngineOptions());
        services.AddSingleton<IConnectivityStateProvider, AlwaysOnlineConnectivityStateProvider>();

        services.AddSingleton<OfflineSyncEngine>(sp =>
        {
            var store = sp.GetRequiredService<IGeoPackageSyncStore>();
            var uploader = sp.GetRequiredService<IOfflineOperationUploader>();
            var options = sp.GetRequiredService<OfflineSyncEngineOptions>();
            return new OfflineSyncEngine(store, uploader, options);
        });
        services.AddSingleton<IOfflineSyncRunner>(sp => sp.GetRequiredService<OfflineSyncEngine>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="BackgroundSyncOrchestrator"/> for periodic background sync.
    /// Requires <see cref="AddHonuaGeoPackageOfflineSync"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Orchestrator options; defaults are used when <see langword="null"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaBackgroundSync(
        this IServiceCollection services,
        BackgroundSyncOrchestratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(options ?? new BackgroundSyncOrchestratorOptions());
        services.AddSingleton<BackgroundSyncOrchestrator>(sp =>
        {
            var runner = sp.GetRequiredService<IOfflineSyncRunner>();
            var connectivity = sp.GetRequiredService<IConnectivityStateProvider>();
            var orchestratorOptions = sp.GetRequiredService<BackgroundSyncOrchestratorOptions>();
            var logger = sp.GetRequiredService<ILogger<BackgroundSyncOrchestrator>>();
            return new BackgroundSyncOrchestrator(runner, connectivity, orchestratorOptions, logger: logger);
        });
        return services;
    }

    /// <summary>
    /// Registers <see cref="IMapAreaDownloader"/> for downloading offline map area packages.
    /// Requires <see cref="AddHonuaGeoPackageOfflineSync"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaMapAreaDownload(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient("HonuaMapArea");
        services.AddSingleton<IMapAreaDownloader>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient("HonuaMapArea");
            var store = sp.GetRequiredService<IGeoPackageSyncStore>();
            return new MapAreaDownloader(httpClient, store);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IHonuaScenePackageDownloader"/> for downloading immutable offline 3D scene packages.
    /// Requires <see cref="AddHonuaGeoPackageOfflineSync"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaScenePackageDownload(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient("HonuaScenePackage");
        services.AddSingleton<IHonuaScenePackageDownloader>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient("HonuaScenePackage");
            var store = sp.GetRequiredService<IGeoPackageSyncStore>();
            return new ScenePackageDownloader(httpClient, store);
        });

        return services;
    }

    /// <summary>
    /// Registers the client-side map annotation layer used by platform map renderers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaMapAnnotations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<HonuaAnnotationLayer>();
        return services;
    }
}
