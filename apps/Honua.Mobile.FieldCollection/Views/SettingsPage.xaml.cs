using Honua.Mobile.FieldCollection.ViewModels;

namespace Honua.Mobile.FieldCollection.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load current settings when page appears
        await _viewModel.LoadSettingsCommand.ExecuteAsync(null);
    }
}