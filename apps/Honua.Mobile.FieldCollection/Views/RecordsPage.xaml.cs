using Honua.Mobile.FieldCollection.ViewModels;

namespace Honua.Mobile.FieldCollection.Views;

public partial class RecordsPage : ContentPage
{
    private readonly RecordsViewModel _viewModel;

    public RecordsPage(RecordsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load metadata and records when page appears
        await _viewModel.LoadMetadataCommand.ExecuteAsync(null);
    }
}
