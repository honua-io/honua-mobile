using Honua.Mobile.Field.Forms;
using Honua.Mobile.Field.Records;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.MapAreas;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui;

public static class HonuaMobileServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaMobileSdk(
        this IServiceCollection services,
        HonuaMobileClientOptions clientOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientOptions);

        services.AddSingleton(clientOptions);
        services.AddSingleton(_ => new HonuaMobileClient(new HttpClient(), clientOptions));
        return services;
    }

    public static IServiceCollection AddHonuaApiOfflineUploader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOfflineOperationUploader, HonuaApiOfflineOperationUploader>();
        return services;
    }

    public static IServiceCollection AddHonuaMobileFieldCollection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<FormValidator>();
        services.AddSingleton<CalculatedFieldEvaluator>();
        services.AddSingleton<RecordWorkflow>();
        services.AddSingleton<DuplicateDetector>();
        return services;
    }

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
            return new BackgroundSyncOrchestrator(runner, connectivity, orchestratorOptions);
        });
        return services;
    }

    public static IServiceCollection AddHonuaMapAreaDownload(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMapAreaDownloader>(sp =>
        {
            var store = sp.GetRequiredService<IGeoPackageSyncStore>();
            return new MapAreaDownloader(new HttpClient(), store);
        });

        return services;
    }
}
