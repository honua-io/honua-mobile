using Honua.Mobile.FieldCollection.ViewModels;

namespace Honua.Mobile.FieldCollection.Views;

public partial class SyncCenterPage : ContentPage
{
    private readonly SyncCenterViewModel _viewModel;

    public SyncCenterPage(SyncCenterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load current sync state and history
        await _viewModel.LoadConflictsCommand.ExecuteAsync(null);
        await _viewModel.LoadSyncHistoryCommand.ExecuteAsync(null);
    }
}