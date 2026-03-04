using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.App;

public sealed partial class MainPage : ContentPage
{
    private readonly IOfflineSyncRunner _syncRunner;
    private readonly IGeoPackageSyncStore _syncStore;

    public MainPage(IOfflineSyncRunner syncRunner, IGeoPackageSyncStore syncStore)
    {
        InitializeComponent();
        _syncRunner = syncRunner;
        _syncStore = syncStore;
    }

    private async void OnRunSyncClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await _syncRunner.SyncAsync();
            StatusLabel.Text = $"Sync complete. Loaded={result.Loaded}, Succeeded={result.Succeeded}, Failed={result.Failed}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Sync error: {ex.Message}";
        }
    }

    private async void OnShowMapAreaCountClicked(object? sender, EventArgs e)
    {
        await _syncStore.InitializeAsync();
        var areas = await _syncStore.ListMapAreasAsync();
        StatusLabel.Text = $"Map areas downloaded: {areas.Count}";
    }
}
