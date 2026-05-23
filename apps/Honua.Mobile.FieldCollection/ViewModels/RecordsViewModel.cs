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
    private readonly IFieldCollectionMetadataService _metadataService;
    private readonly ILocalRecordExportService _recordExportService;
    private bool _updatingSelection;

    [ObservableProperty]
    private LayerInfo? selectedLayer;

    [ObservableProperty]
    private FieldProjectInfo? selectedProject;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool showPendingOnly;

    [ObservableProperty]
    private int totalRecordCount;

    [ObservableProperty]
    private int pendingRecordCount;

    public ObservableCollection<FieldProjectInfo> AvailableProjects { get; } = new();
    public ObservableCollection<LayerInfo> AvailableLayers { get; } = new();
    public ObservableCollection<Feature> Records { get; } = new();

    public RecordsViewModel(
        INavigationService navigationService,
        IFeatureService featureService,
        IFormService formService,
        IFieldCollectionMetadataService metadataService,
        ILocalRecordExportService recordExportService)
        : base(navigationService)
    {
        _featureService = featureService;
        _formService = formService;
        _metadataService = metadataService;
        _recordExportService = recordExportService;

        Title = "Records";
    }

    partial void OnSelectedLayerChanged(LayerInfo? value)
    {
        if (_updatingSelection || value == null)
        {
            return;
        }

        _ = LoadRecords();
    }

    partial void OnSelectedProjectChanged(FieldProjectInfo? value)
    {
        if (_updatingSelection || value == null)
        {
            return;
        }

        _ = SelectProject(value);
    }

    protected override async Task OnRefresh()
    {
        await LoadMetadataAsync(refresh: true);
    }

    [RelayCommand]
    private Task LoadMetadata()
    {
        return LoadMetadataAsync();
    }

    private async Task LoadMetadataAsync(bool refresh = false)
    {
        await ExecuteAsync(async () =>
        {
            var projects = await _metadataService.GetProjectsAsync(refresh);
            var selectedProject = await _metadataService.GetSelectedProjectAsync();
            var layers = await _metadataService.GetLayersAsync(refresh);

            ApplyMetadata(projects, selectedProject, layers);
        });

        await LoadRecords();
    }

    private void ApplyMetadata(
        IReadOnlyList<FieldProjectInfo> projects,
        FieldProjectInfo? selectedProject,
        IReadOnlyList<LayerInfo> layers)
    {
        var selectedLayerId = SelectedLayer?.Id;

        _updatingSelection = true;
        try
        {
            AvailableProjects.Clear();
            foreach (var project in projects)
            {
                AvailableProjects.Add(project);
            }

            SelectedProject = selectedProject is null
                ? AvailableProjects.FirstOrDefault()
                : AvailableProjects.FirstOrDefault(project =>
                    string.Equals(project.ServiceId, selectedProject.ServiceId, StringComparison.OrdinalIgnoreCase)) ?? selectedProject;

            AvailableLayers.Clear();
            foreach (var layer in layers)
            {
                AvailableLayers.Add(layer);
            }

            SelectedLayer = AvailableLayers.FirstOrDefault(layer => layer.Id == selectedLayerId) ??
                AvailableLayers.FirstOrDefault();
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    [RelayCommand]
    private async Task LoadRecords()
    {
        if (SelectedLayer == null)
        {
            Records.Clear();
            TotalRecordCount = 0;
            PendingRecordCount = 0;
            return;
        }

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
    private async Task SelectProject(FieldProjectInfo project)
    {
        if (SelectedProject != project)
        {
            SelectedProject = project;
        }

        await _metadataService.SelectProjectAsync(project.ServiceId);
        await LoadMetadataAsync(refresh: true);
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
        if (SelectedLayer == null)
        {
            await ShowError("Cannot Export", "Please select a layer first.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _recordExportService.ExportLayerAsync(SelectedLayer);
            await ShowMessage(
                "Export Complete",
                $"Exported {result.RecordCount} records and {result.AttachmentCount} attachment references to {result.ExportDirectory}.");
        });
    }
}
