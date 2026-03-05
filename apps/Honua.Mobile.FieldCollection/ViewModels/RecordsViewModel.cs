using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using System.Collections.ObjectModel;

namespace Honua.Mobile.FieldCollection.ViewModels;

public partial class RecordsViewModel : BaseViewModel
{
    private readonly IFeatureService _featureService;
    private readonly IFormService _formService;

    [ObservableProperty]
    private LayerInfo? selectedLayer;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool showPendingOnly;

    [ObservableProperty]
    private int totalRecordCount;

    [ObservableProperty]
    private int pendingRecordCount;

    public ObservableCollection<LayerInfo> AvailableLayers { get; } = new();
    public ObservableCollection<Feature> Records { get; } = new();

    public RecordsViewModel(
        INavigationService navigationService,
        IFeatureService featureService,
        IFormService formService)
        : base(navigationService)
    {
        _featureService = featureService;
        _formService = formService;

        Title = "Records";

        // Initialize with demo layers
        InitializeLayers();
    }

    private void InitializeLayers()
    {
        AvailableLayers.Add(new LayerInfo
        {
            Id = 1,
            Name = "Points of Interest",
            Description = "Sample point layer for demonstration",
            GeometryType = GeometryType.Point,
            IsVisible = true,
            IsEditable = true
        });

        AvailableLayers.Add(new LayerInfo
        {
            Id = 2,
            Name = "Inspection Routes",
            Description = "Line features for route planning",
            GeometryType = GeometryType.LineString,
            IsVisible = false,
            IsEditable = true
        });

        // Select the first layer by default
        if (AvailableLayers.Count > 0)
        {
            SelectedLayer = AvailableLayers[0];
        }
    }

    protected override async Task OnRefresh()
    {
        await LoadRecords();
    }

    [RelayCommand]
    private async Task LoadRecords()
    {
        if (SelectedLayer == null) return;

        await ExecuteAsync(async () =>
        {
            var features = await _featureService.GetFeaturesAsync(SelectedLayer.Id);

            // Apply search filter
            if (!string.IsNullOrEmpty(SearchText))
            {
                features = features.Where(f =>
                    f.Attributes.Values.Any(v => v?.ToString()?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();
            }

            // Apply pending filter
            if (ShowPendingOnly)
            {
                features = features.Where(f => f.IsPendingSync).ToList();
            }

            Records.Clear();
            foreach (var feature in features)
            {
                Records.Add(feature);
            }

            TotalRecordCount = Records.Count;
            PendingRecordCount = Records.Count(r => r.IsPendingSync);
        });
    }

    [RelayCommand]
    private async Task SelectLayer(LayerInfo layer)
    {
        if (SelectedLayer == layer) return;

        SelectedLayer = layer;
        await LoadRecords();
    }

    [RelayCommand]
    private async Task SearchRecords()
    {
        await LoadRecords();
    }

    [RelayCommand]
    private async Task ClearSearch()
    {
        SearchText = string.Empty;
        await LoadRecords();
    }

    [RelayCommand]
    private async Task TogglePendingFilter()
    {
        ShowPendingOnly = !ShowPendingOnly;
        await LoadRecords();
    }

    [RelayCommand]
    private async Task ViewRecord(Feature record)
    {
        var parameters = new Dictionary<string, object>
        {
            ["featureId"] = record.Id,
            ["layerId"] = record.LayerId
        };

        await NavigationService.NavigateToAsync("record-detail", parameters);
    }

    [RelayCommand]
    private async Task EditRecord(Feature record)
    {
        var parameters = new Dictionary<string, object>
        {
            ["featureId"] = record.Id,
            ["layerId"] = record.LayerId,
            ["isEdit"] = true
        };

        await NavigationService.NavigateToAsync("record-edit", parameters);
    }

    [RelayCommand]
    private async Task CreateNewRecord()
    {
        if (SelectedLayer == null || !SelectedLayer.IsEditable)
        {
            await ShowError("Cannot Create Record", "Please select an editable layer first.");
            return;
        }

        var parameters = new Dictionary<string, object>
        {
            ["layerId"] = SelectedLayer.Id,
            ["isNew"] = true
        };

        await NavigationService.NavigateToAsync("record-create", parameters);
    }

    [RelayCommand]
    private async Task DeleteRecord(Feature record)
    {
        var confirmed = await ShowConfirmation("Delete Record",
            $"Are you sure you want to delete this {SelectedLayer?.Name} record? This action cannot be undone.",
            "Delete", "Cancel");

        if (confirmed)
        {
            await ExecuteAsync(async () =>
            {
                await _featureService.DeleteFeatureAsync(record.LayerId, record.Id);
                Records.Remove(record);
                await ShowMessage("Record Deleted", "The record has been deleted successfully.");
            });
        }
    }

    [RelayCommand]
    private async Task ExportRecords()
    {
        if (Records.Count == 0)
        {
            await ShowMessage("No Records", "There are no records to export for the selected layer.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            // In a real implementation, this would export to various formats
            await Task.Delay(2000); // Simulate export process
            await ShowMessage("Export Complete",
                $"Exported {Records.Count} records from {SelectedLayer?.Name} layer.");
        });
    }
}