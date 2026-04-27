using Honua.Mobile.FieldCollection.Views;

namespace Honua.Mobile.FieldCollection;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register additional routes for navigation
        RegisterRoutes();
    }

    private static void RegisterRoutes()
    {
        Routing.RegisterRoute("record-detail", typeof(RecordDetailPage));
        Routing.RegisterRoute("record-edit", typeof(RecordEditPage));
        Routing.RegisterRoute("record-create", typeof(RecordEditPage));
        Routing.RegisterRoute("authentication", typeof(AuthenticationPage));
        Routing.RegisterRoute("diagnostics", typeof(DiagnosticsPage));

        Routing.RegisterRoute("map/layer-settings", typeof(LayerSettingsPage));
        Routing.RegisterRoute("map/feature-detail", typeof(FeatureDetailPage));

        Routing.RegisterRoute("sync/conflict-resolution", typeof(ConflictResolutionPage));
        Routing.RegisterRoute("sync/sync-history", typeof(SyncHistoryPage));

        Routing.RegisterRoute("settings/server-config", typeof(ServerConfigPage));
        Routing.RegisterRoute("settings/user-profile", typeof(UserProfilePage));
        Routing.RegisterRoute("settings/about", typeof(AboutPage));
    }
}
