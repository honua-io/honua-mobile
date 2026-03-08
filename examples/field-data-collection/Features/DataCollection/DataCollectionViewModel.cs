// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace HonuaFieldCollector.Features.DataCollection;

/// <summary>
/// ViewModel for the Record detail / edit screen.
/// Loads a form definition, renders dynamic fields, validates, and submits.
/// </summary>
public partial class DataCollectionViewModel : ObservableObject
{
    private readonly IFormService _formService;
    private readonly IDataCollectionService _dataCollectionService;
    private readonly IValidationService _validationService;
    private readonly ILocationService _locationService;
    private readonly ILogger<DataCollectionViewModel> _logger;

    [ObservableProperty] private string _formTitle = "New Record";
    [ObservableProperty] private string _formStatusText = "Draft";
    [ObservableProperty] private Color _formStatusColor = Colors.Gray;
    [ObservableProperty] private string _locationText = "Acquiring GPS...";
    [ObservableProperty] private bool _hasAttachments;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<FormFieldViewModel> FormFields { get; } = [];
    public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];

    private string? _formId;
    private string? _featureId;

    public DataCollectionViewModel(
        IFormService formService,
        IDataCollectionService dataCollectionService,
        IValidationService validationService,
        ILocationService locationService,
        ILogger<DataCollectionViewModel> logger)
    {
        _formService = formService;
        _dataCollectionService = dataCollectionService;
        _validationService = validationService;
        _locationService = locationService;
        _logger = logger;
    }

    public async Task InitializeAsync(string? formId, string? featureId)
    {
        _formId = formId;
        _featureId = featureId;
        IsLoading = true;

        try
        {
            // Load form definition
            var definition = await _formService.GetFormDefinitionAsync(formId ?? "default");
            FormTitle = definition.Title;

            // Build field view models from definition
            FormFields.Clear();
            foreach (var control in definition.Controls)
            {
                FormFields.Add(new FormFieldViewModel(control));
            }

            // If editing an existing feature, populate values
            if (featureId is not null)
            {
                var existing = await _dataCollectionService.GetRecordAsync(featureId);
                if (existing is not null)
                {
                    foreach (var field in FormFields)
                        field.LoadValue(existing.FieldValues);

                    FormStatusText = "Editing";
                    FormStatusColor = Colors.Orange;
                }
            }

            // Capture location
            await RefreshLocationAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize data collection form");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLocationAsync()
    {
        try
        {
            var location = await _locationService.GetCurrentLocationAsync();
            if (location is not null)
                LocationText = $"{location.Latitude:F6}, {location.Longitude:F6} ({location.Accuracy:F1}m)";
            else
                LocationText = "Location unavailable";
        }
        catch (Exception ex)
        {
            LocationText = "GPS error";
            _logger.LogWarning(ex, "Failed to get location");
        }
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        try
        {
            var values = CollectFieldValues();
            await _dataCollectionService.SaveDraftAsync(_formId, _featureId, values);
            FormStatusText = "Draft Saved";
            FormStatusColor = Colors.Green;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save draft");
            await Shell.Current.DisplayAlert("Error", "Failed to save draft.", "OK");
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        // Validate all fields
        var issues = _validationService.ValidateAll(FormFields.Select(f => f.ToFieldData()).ToList());
        if (issues.Any(i => i.IsError))
        {
            foreach (var field in FormFields)
                field.ApplyValidationIssues(issues);
            await Shell.Current.DisplayAlert("Validation", "Please fix errors before submitting.", "OK");
            return;
        }

        try
        {
            var values = CollectFieldValues();
            await _dataCollectionService.SubmitAsync(_formId, _featureId, values);
            FormStatusText = "Submitted";
            FormStatusColor = Colors.Green;
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit record");
            await Shell.Current.DisplayAlert("Error", "Submission failed. Record saved as draft.", "OK");
            await SaveDraftAsync();
        }
    }

    private Dictionary<string, object?> CollectFieldValues()
    {
        var values = new Dictionary<string, object?>();
        foreach (var field in FormFields)
            values[field.FieldId] = field.GetValue();
        return values;
    }
}

/// <summary>
/// ViewModel for a single dynamic form field.
/// </summary>
public partial class FormFieldViewModel : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _placeholder = string.Empty;
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private string _validationError = string.Empty;
    [ObservableProperty] private bool _hasValidationError;

    // Input type flags
    [ObservableProperty] private bool _isTextInput;
    [ObservableProperty] private bool _isNumericInput;
    [ObservableProperty] private bool _isDateInput;
    [ObservableProperty] private bool _isSelectInput;
    [ObservableProperty] private bool _isPhotoInput;
    [ObservableProperty] private bool _isLocationInput;

    // Values
    [ObservableProperty] private string _textValue = string.Empty;
    [ObservableProperty] private string _numericValue = string.Empty;
    [ObservableProperty] private DateTime _dateValue = DateTime.Today;
    [ObservableProperty] private string? _selectedOption;
    [ObservableProperty] private string _photoStatusText = "No photo";
    [ObservableProperty] private string _locationValueText = "Not captured";

    public string FieldId { get; }
    public bool IsEditable => !IsReadOnly;
    public List<string> PickerOptions { get; } = [];

    public FormFieldViewModel(object controlDefinition)
    {
        // Populated from the form definition's control type
        FieldId = string.Empty;
        // Real implementation maps control definition to the appropriate input type
    }

    public void LoadValue(Dictionary<string, object?> values)
    {
        if (values.TryGetValue(FieldId, out var val) && val is not null)
        {
            if (IsTextInput) TextValue = val.ToString() ?? string.Empty;
            else if (IsNumericInput) NumericValue = val.ToString() ?? string.Empty;
        }
    }

    public object? GetValue()
    {
        if (IsTextInput) return TextValue;
        if (IsNumericInput) return double.TryParse(NumericValue, out var n) ? n : null;
        if (IsDateInput) return DateValue;
        if (IsSelectInput) return SelectedOption;
        return null;
    }

    public object ToFieldData() => new { FieldId, Value = GetValue(), IsRequired };

    public void ApplyValidationIssues(IEnumerable<object> issues) { /* wire per-field errors */ }

    [RelayCommand]
    private async Task CapturePhotoAsync()
    {
        var photo = await MediaPicker.Default.CapturePhotoAsync();
        if (photo is not null)
            PhotoStatusText = photo.FileName;
    }

    [RelayCommand]
    private async Task CaptureLocationAsync()
    {
        var loc = await Geolocation.Default.GetLocationAsync();
        if (loc is not null)
            LocationValueText = $"{loc.Latitude:F6}, {loc.Longitude:F6}";
    }
}

/// <summary>
/// ViewModel for a photo/file attachment thumbnail.
/// </summary>
public partial class AttachmentViewModel : ObservableObject
{
    [ObservableProperty] private string _thumbnailSource = string.Empty;
    [ObservableProperty] private string _fileName = string.Empty;
}
