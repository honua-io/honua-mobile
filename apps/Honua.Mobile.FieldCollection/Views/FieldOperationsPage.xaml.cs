using Honua.Mobile.FieldCollection.ViewModels;

namespace Honua.Mobile.FieldCollection.Views;

public partial class FieldOperationsPage : ContentPage
{
    private readonly FieldOperationsViewModel _viewModel;

    public FieldOperationsPage(FieldOperationsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadWorkspaceCommand.ExecuteAsync(null);
    }
}
