using Honua.Mobile.Field.Capture;
using Honua.Mobile.Maui.Auth;
using Honua.Mobile.Maui.Annotations;
using Honua.Mobile.Maui.Display;
using Honua.Mobile.Maui.Location;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.MapAreas;
using Honua.Mobile.Offline.ScenePackages;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Abstractions.Routing;
using Honua.Sdk.Abstractions.Scenes;
using Honua.Sdk.Field.Records;
using Honua.Sdk.GeoServices.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SdkFeatureClient = Honua.Mobile.Sdk.Features.HonuaMobileSdkFeatureClient;
using SdkOfflineChangeJournal = Honua.Sdk.Offline.Abstractions.IOfflineChangeJournal;
using SdkOfflineFeatureStore = Honua.Sdk.Offline.Abstractions.IOfflineFeatureStore;
using SdkOfflinePackageManifest = Honua.Sdk.Offline.Abstractions.OfflinePackageManifest;
using SdkOfflineStateStore = Honua.Sdk.Offline.Abstractions.IOfflineSyncStateStore;
using SdkOfflineCheckpointStore = Honua.Sdk.Offline.Abstractions.IOfflineSyncCheckpointStore;
using SdkOfflineSyncEngine = Honua.Sdk.Offline.OfflineSyncEngine;
using SdkOfflineSyncEngineOptions = Honua.Sdk.Offline.OfflineSyncEngineOptions;

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
            var authTokenProvider = sp.GetService<IAuthTokenProvider>();
            return new HonuaMobileClient(httpClient, clientOptions, authTokenProvider);
        });
        return services;
    }

    /// <summary>
    /// Registers mobile auth token storage and refresh services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tokenStore">Optional secure token store. When omitted, an in-memory store is registered.</param>
    /// <param name="options">Optional token refresh options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaMobileAuth(
        this IServiceCollection services,
        IAuthTokenStore? tokenStore = null,
        RefreshingAuthTokenProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (tokenStore is null)
        {
            services.AddSingleton<IAuthTokenStore, InMemoryAuthTokenStore>();
        }
        else
        {
            services.AddSingleton<IAuthTokenStore>(tokenStore);
        }

        services.AddSingleton(options ?? new RefreshingAuthTokenProviderOptions());
        services.AddHttpClient("HonuaMobileAuth");
        services.AddSingleton<IAuthTokenProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new RefreshingAuthTokenProvider(
                sp.GetRequiredService<IAuthTokenStore>(),
                factory.CreateClient("HonuaMobileAuth"),
                sp.GetRequiredService<RefreshingAuthTokenProviderOptions>());
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
    /// Registers SDK routing clients from the configured <see cref="HonuaMobileClient"/>.
    /// Requires <see cref="AddHonuaMobileSdk"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp => sp.GetRequiredService<HonuaMobileClient>().Routing);
        services.AddSingleton<IHonuaRoutingClient>(sp => sp.GetRequiredService<HonuaMobileClient>().Routing);
        return services;
    }

    /// <summary>
    /// Registers <see cref="IHonuaSceneClient"/> from the configured <see cref="HonuaMobileClient"/>.
    /// Requires <see cref="AddHonuaMobileSdk"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaScenes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHonuaSceneClient>(sp => sp.GetRequiredService<HonuaMobileClient>().Scenes);
        return services;
    }

    /// <summary>
    /// Registers field data-collection services backed by SDK-owned field contracts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaMobileFieldCollection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DuplicateDetector>();
        services.AddSingleton<MobileFieldCaptureWorkflow>();
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
    /// Registers the SDK-owned offline sync engine with mobile GeoPackage storage adapters.
    /// Requires <see cref="AddHonuaMobileSdk"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="storeOptions">GeoPackage store configuration.</param>
    /// <param name="manifest">SDK offline package manifest to sync.</param>
    /// <param name="syncOptions">SDK sync engine options; defaults are used when <see langword="null"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaSdkGeoPackageOfflineSync(
        this IServiceCollection services,
        GeoPackageSyncStoreOptions storeOptions,
        SdkOfflinePackageManifest manifest,
        SdkOfflineSyncEngineOptions? syncOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(manifest);

        services.AddSingleton(storeOptions);
        services.AddSingleton(manifest);
        services.AddSingleton(syncOptions ?? new SdkOfflineSyncEngineOptions());
        services.AddSingleton<IGeoPackageSyncStore, GeoPackageSyncStore>();
        services.AddSingleton<IConnectivityStateProvider, AlwaysOnlineConnectivityStateProvider>();
        services.AddSingleton<GeoPackageSdkOfflineStoreAdapter>();
        services.AddSingleton<SdkFeatureClient>();
        services.AddSingleton<HonuaMobileSdkFeatureClient>();
        services.AddSingleton<IHonuaFeatureQueryClient>(sp => sp.GetRequiredService<SdkFeatureClient>());
        services.AddSingleton<IHonuaFeatureEditClient>(sp => sp.GetRequiredService<SdkFeatureClient>());
        services.AddSingleton<IHonuaFeatureAttachmentClient>(sp => sp.GetRequiredService<SdkFeatureClient>());
        services.AddSingleton<SdkOfflineFeatureStore>(sp => sp.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>());
        services.AddSingleton<SdkOfflineChangeJournal>(sp => sp.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>());
        services.AddSingleton<SdkOfflineCheckpointStore>(sp => sp.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>());
        services.AddSingleton<SdkOfflineStateStore>(sp => sp.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>());

        services.AddSingleton<SdkOfflineSyncEngine>(sp => new SdkOfflineSyncEngine(
            sp.GetRequiredService<IHonuaFeatureQueryClient>(),
            sp.GetRequiredService<IHonuaFeatureEditClient>(),
            sp.GetRequiredService<SdkOfflineFeatureStore>(),
            sp.GetRequiredService<SdkOfflineChangeJournal>(),
            sp.GetRequiredService<SdkOfflineCheckpointStore>(),
            sp.GetRequiredService<SdkOfflineSyncEngineOptions>(),
            sp.GetRequiredService<SdkOfflineStateStore>()));

        services.AddSingleton<SdkOfflineSyncRunner>();
        services.AddSingleton<IOfflineSyncRunner>(sp => sp.GetRequiredService<SdkOfflineSyncRunner>());

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
    /// Registers the cancellable background prefetch scheduler used for lifecycle-aware cache warming.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Scheduler options; defaults are used when <see langword="null"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaBackgroundPrefetch(
        this IServiceCollection services,
        BackgroundPrefetchSchedulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(options ?? new BackgroundPrefetchSchedulerOptions());
        services.AddSingleton<BackgroundPrefetchScheduler>(sp => new BackgroundPrefetchScheduler(
            sp.GetRequiredService<BackgroundPrefetchSchedulerOptions>(),
            sp.GetService<ILogger<BackgroundPrefetchScheduler>>()));
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

    /// <summary>
    /// Registers the dependency-free native map display controller.
    /// Applications must also register an <see cref="IHonuaNativeMapAdapter"/> and an SDK
    /// <see cref="IHonuaFeatureQueryClient"/> for feature-backed layers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaNativeDisplay(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<HonuaNativeMapDisplayController>();
        return services;
    }

    /// <summary>
    /// Registers device location orchestration over app-provided native permission and location adapters.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHonuaDeviceLocation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp => new HonuaDeviceLocationCoordinator(
            sp.GetRequiredService<IHonuaDeviceLocationPermissionService>(),
            sp.GetRequiredService<IHonuaDeviceLocationProvider>(),
            sp.GetService<IHonuaBackgroundLocationProvider>(),
            sp.GetService<IHonuaGeofenceMonitor>()));
        return services;
    }
}
