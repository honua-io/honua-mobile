// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Honua.Mobile.SharedComponents.Components.Forms;

/// <summary>
/// Revolutionary dynamic form component that generates mobile-optimized forms from server schemas.
/// Competes directly with Fulcrum and Survey123 with superior offline capabilities and native performance.
/// </summary>
public partial class HonuaFeatureFormViewModel : ObservableObject
{
    private readonly ILogger<HonuaFeatureFormViewModel> _logger;
    private readonly IFormSchemaService _schemaService;
    private readonly IValidationService _validationService;
    private readonly ICameraService _cameraService;
    private readonly ILocationService _locationService;
    private readonly ISensorService _sensorService;

    [ObservableProperty]
    private string _formId = string.Empty;

    [ObservableProperty]
    private string _formTitle = "Honua Data Collection";

    [ObservableProperty]
    private string _formDescription = string.Empty;

    [ObservableProperty]
    private bool _hasDescription = false;

    [ObservableProperty]
    private bool _showProgress = true;

    [ObservableProperty]
    private bool _allowDrafts = true;

    [ObservableProperty]
    private double _completionProgress = 0.0;

    [ObservableProperty]
    private string _submitButtonText = "Submit Form";

    [ObservableProperty]
    private bool _canSubmit = false;

    [ObservableProperty]
    private bool _hasValidationErrors = false;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private FormMode _mode = FormMode.Create;

    [ObservableProperty]
    private string _featureId = string.Empty;

    public ObservableCollection<IFormField> FormFields { get; } = new();
    public ObservableCollection<string> ValidationErrors { get; } = new();

    public double CompletionPercentage => CompletionProgress * 100;

    public HonuaFeatureFormViewModel(
        ILogger<HonuaFeatureFormViewModel> logger,
        IFormSchemaService schemaService,
        IValidationService validationService,
        ICameraService cameraService,
        ILocationService locationService,
        ISensorService sensorService)
    {
        _logger = logger;
        _schemaService = schemaService;
        _validationService = validationService;
        _cameraService = cameraService;
        _locationService = locationService;
        _sensorService = sensorService;

        // Subscribe to field changes for real-time validation
        FormFields.CollectionChanged += OnFormFieldsChanged;
    }

    /// <summary>
    /// Loads a form schema and generates the dynamic UI.
    /// This is where the magic happens - server schema becomes native mobile UI.
    /// </summary>
    public async Task LoadFormSchemaAsync(string formId, string? featureId = null)
    {
        try
        {
            IsLoading = true;
            _logger.LogInformation("Loading form schema: {FormId}", formId);

            FormId = formId;
            FeatureId = featureId ?? string.Empty;
            Mode = string.IsNullOrEmpty(featureId) ? FormMode.Create : FormMode.Edit;

            // Load schema from server or cache
            var schema = await _schemaService.GetFormSchemaAsync(formId);
            if (schema == null)
            {
                throw new InvalidOperationException($"Form schema not found: {formId}");
            }

            // Update form metadata
            FormTitle = schema.Title;
            FormDescription = schema.Description ?? string.Empty;
            HasDescription = !string.IsNullOrEmpty(FormDescription);
            SubmitButtonText = Mode == FormMode.Edit ? "Update Feature" : "Create Feature";

            // Clear existing fields
            FormFields.Clear();
            ValidationErrors.Clear();

            // Generate form fields from schema
            await GenerateFormFieldsAsync(schema);

            // Load existing data if editing
            if (Mode == FormMode.Edit && !string.IsNullOrEmpty(FeatureId))
            {
                await LoadExistingDataAsync(FeatureId);
            }

            // Initial validation
            ValidateForm();

            _logger.LogInformation("Form schema loaded successfully: {FieldCount} fields", FormFields.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load form schema: {FormId}", formId);
            await ShowErrorAsync("Form Error", $"Failed to load form: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Generates form fields from the schema definition.
    /// Supports all major field types with platform-optimized UX.
    /// </summary>
    private async Task GenerateFormFieldsAsync(FormSchema schema)
    {
        foreach (var fieldDef in schema.Fields)
        {
            var field = await CreateFormFieldAsync(fieldDef);
            if (field != null)
            {
                // Subscribe to field changes
                field.PropertyChanged += OnFieldPropertyChanged;
                FormFields.Add(field);
            }
        }
    }

    /// <summary>
    /// Creates a specific form field based on the field definition.
    /// This factory method handles all supported field types.
    /// </summary>
    private async Task<IFormField?> CreateFormFieldAsync(FormFieldDefinition fieldDef)
    {
        return fieldDef.Type.ToLowerInvariant() switch
        {
            "text" => new TextFormField(fieldDef),
            "textarea" => new TextAreaFormField(fieldDef),
            "number" => new NumberFormField(fieldDef),
            "email" => new EmailFormField(fieldDef),
            "phone" => new PhoneFormField(fieldDef),
            "url" => new UrlFormField(fieldDef),
            "date" => new DateFormField(fieldDef),
            "time" => new TimeFormField(fieldDef),
            "datetime" => new DateTimeFormField(fieldDef),
            "picker" => await CreatePickerFieldAsync(fieldDef),
            "radio" => new RadioFormField(fieldDef),
            "checkbox" => new CheckboxFormField(fieldDef),
            "switch" => new SwitchFormField(fieldDef),
            "slider" => new SliderFormField(fieldDef),
            "photo" => new PhotoFormField(fieldDef, _cameraService),
            "signature" => new SignatureFormField(fieldDef),
            "location" => new LocationFormField(fieldDef, _locationService),
            "barcode" => new BarcodeFormField(fieldDef),
            "sensor" => await CreateSensorFieldAsync(fieldDef),
            "calculation" => new CalculatedFormField(fieldDef),
            "section" => new SectionFormField(fieldDef),
            _ => new TextFormField(fieldDef) // Fallback to text field
        };
    }

    /// <summary>
    /// Creates a picker field with dynamic options from server or static list.
    /// </summary>
    private async Task<PickerFormField> CreatePickerFieldAsync(FormFieldDefinition fieldDef)
    {
        var pickerField = new PickerFormField(fieldDef);

        // Load options from various sources
        if (fieldDef.Options?.DataSource != null)
        {
            try
            {
                var options = await _schemaService.LoadPickerOptionsAsync(fieldDef.Options.DataSource);
                pickerField.SetOptions(options);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load picker options from: {DataSource}", fieldDef.Options.DataSource);
                // Fall back to static options if available
                if (fieldDef.Options.StaticItems?.Any() == true)
                {
                    pickerField.SetOptions(fieldDef.Options.StaticItems);
                }
            }
        }
        else if (fieldDef.Options?.StaticItems?.Any() == true)
        {
            pickerField.SetOptions(fieldDef.Options.StaticItems);
        }

        return pickerField;
    }

    /// <summary>
    /// Creates a sensor field with IoT integration capabilities.
    /// This is revolutionary - automated sensor data collection in forms!
    /// </summary>
    private async Task<SensorFormField> CreateSensorFieldAsync(FormFieldDefinition fieldDef)
    {
        var sensorField = new SensorFormField(fieldDef, _sensorService);

        // Auto-configure sensor if specified
        if (fieldDef.Sensor?.AutoConnect == true)
        {
            try
            {
                await sensorField.ConnectToSensorAsync(fieldDef.Sensor.SensorType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-connect to sensor: {SensorType}", fieldDef.Sensor.SensorType);
            }
        }

        return sensorField;
    }

    /// <summary>
    /// Loads existing data for editing mode.
    /// </summary>
    private async Task LoadExistingDataAsync(string featureId)
    {
        try
        {
            var existingData = await _schemaService.LoadExistingFeatureDataAsync(FormId, featureId);
            if (existingData != null)
            {
                // Populate form fields with existing values
                foreach (var field in FormFields)
                {
                    if (existingData.TryGetValue(field.Id, out var value))
                    {
                        field.SetValue(value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load existing data for feature: {FeatureId}", featureId);
        }
    }

    #region Commands

    [RelayCommand]
    private async Task SubmitFormAsync()
    {
        try
        {
            _logger.LogInformation("Submitting form: {FormId}", FormId);

            // Final validation
            if (!ValidateForm())
            {
                await ShowErrorAsync("Validation Error", "Please fix all errors before submitting");
                return;
            }

            IsLoading = true;
            CanSubmit = false;

            // Collect form data
            var formData = CollectFormData();

            // Submit to server
            var result = Mode == FormMode.Edit
                ? await _schemaService.UpdateFeatureAsync(FormId, FeatureId, formData)
                : await _schemaService.CreateFeatureAsync(FormId, formData);

            if (result.Success)
            {
                _logger.LogInformation("Form submitted successfully: {FormId}", FormId);
                await ShowSuccessAsync("Success", "Data saved successfully!");

                // Notify parent of successful submission
                FormSubmitted?.Invoke(this, new FormSubmittedEventArgs(result.FeatureId, formData));
            }
            else
            {
                _logger.LogWarning("Form submission failed: {ErrorMessage}", result.ErrorMessage);
                await ShowErrorAsync("Submission Error", result.ErrorMessage ?? "Failed to save data");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Form submission error: {FormId}", FormId);
            await ShowErrorAsync("Error", "An unexpected error occurred while saving data");
        }
        finally
        {
            IsLoading = false;
            ValidateForm(); // Re-enable submit button if valid
        }
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        try
        {
            _logger.LogInformation("Saving draft: {FormId}", FormId);

            var formData = CollectFormData();
            await _schemaService.SaveDraftAsync(FormId, FeatureId, formData);

            await ShowSuccessAsync("Draft Saved", "Your progress has been saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save draft: {FormId}", FormId);
            await ShowErrorAsync("Error", "Failed to save draft");
        }
    }

    [RelayCommand]
    private async Task CapturePhotoAsync(IFormField field)
    {
        if (field is not PhotoFormField photoField) return;

        try
        {
            _logger.LogInformation("Capturing photo for field: {FieldId}", field.Id);

            var photo = await _cameraService.CapturePhotoAsync(new CameraOptions
            {
                IncludeLocation = photoField.RequireLocation,
                EnablePrivacyBlur = photoField.EnablePrivacyBlur,
                Quality = photoField.PhotoQuality
            });

            if (photo != null)
            {
                photoField.AddPhoto(photo);
                ValidateField(field);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Photo capture failed for field: {FieldId}", field.Id);
            await ShowErrorAsync("Camera Error", "Failed to capture photo");
        }
    }

    [RelayCommand]
    private async Task UpdateLocationAsync(IFormField field)
    {
        if (field is not LocationFormField locationField) return;

        try
        {
            _logger.LogInformation("Updating location for field: {FieldId}", field.Id);

            var location = await _locationService.GetCurrentLocationAsync(new LocationOptions
            {
                Accuracy = locationField.RequiredAccuracy,
                Timeout = TimeSpan.FromSeconds(30)
            });

            if (location != null)
            {
                locationField.SetLocation(location);
                ValidateField(field);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Location update failed for field: {FieldId}", field.Id);
            await ShowErrorAsync("Location Error", "Failed to get current location");
        }
    }

    [RelayCommand]
    private async Task ReadSensorAsync(IFormField field)
    {
        if (field is not SensorFormField sensorField) return;

        try
        {
            _logger.LogInformation("Reading sensor for field: {FieldId}", field.Id);

            var reading = await sensorField.ReadSensorAsync();
            if (reading != null)
            {
                ValidateField(field);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sensor reading failed for field: {FieldId}", field.Id);
            await ShowErrorAsync("Sensor Error", "Failed to read sensor data");
        }
    }

    [RelayCommand]
    private async Task ValidateFieldAsync(IFormField field)
    {
        await Task.Run(() => ValidateField(field));
        ValidateForm();
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates the entire form and updates UI state.
    /// </summary>
    private bool ValidateForm()
    {
        ValidationErrors.Clear();
        var isValid = true;
        var completedFields = 0;

        foreach (var field in FormFields)
        {
            var fieldValid = ValidateField(field);
            if (!fieldValid)
            {
                isValid = false;
                if (!string.IsNullOrEmpty(field.ValidationMessage))
                {
                    ValidationErrors.Add($"{field.Label}: {field.ValidationMessage}");
                }
            }

            if (field.HasValue)
            {
                completedFields++;
            }
        }

        // Update UI state
        CanSubmit = isValid && !IsLoading;
        HasValidationErrors = !isValid;
        CompletionProgress = FormFields.Count > 0 ? (double)completedFields / FormFields.Count : 0;

        return isValid;
    }

    /// <summary>
    /// Validates a specific field.
    /// </summary>
    private bool ValidateField(IFormField field)
    {
        var result = _validationService.ValidateField(field);
        field.SetValidationResult(result);
        return result.IsValid;
    }

    #endregion

    #region Data Collection

    /// <summary>
    /// Collects all form data for submission.
    /// </summary>
    private Dictionary<string, object> CollectFormData()
    {
        var data = new Dictionary<string, object>();

        foreach (var field in FormFields)
        {
            var value = field.GetSubmissionValue();
            if (value != null)
            {
                data[field.Id] = value;
            }
        }

        // Add metadata
        data["_formId"] = FormId;
        data["_submittedAt"] = DateTimeOffset.UtcNow;
        data["_deviceId"] = DeviceInfo.Current.Idiom.ToString();
        data["_appVersion"] = AppInfo.Current.VersionString;

        return data;
    }

    #endregion

    #region Event Handlers

    private void OnFormFieldsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ValidateForm();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IFormField.Value))
        {
            ValidateForm();
        }
    }

    #endregion

    #region Helper Methods

    private static async Task ShowErrorAsync(string title, string message)
    {
        if (Application.Current?.MainPage != null)
        {
            await Application.Current.MainPage.DisplayAlert(title, message, "OK");
        }
    }

    private static async Task ShowSuccessAsync(string title, string message)
    {
        if (Application.Current?.MainPage != null)
        {
            await Application.Current.MainPage.DisplayAlert(title, message, "OK");
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Fired when the form is successfully submitted.
    /// </summary>
    public event EventHandler<FormSubmittedEventArgs>? FormSubmitted;

    #endregion
}

/// <summary>
/// Form operation modes.
/// </summary>
public enum FormMode
{
    Create,
    Edit,
    View
}

/// <summary>
/// Event args for successful form submission.
/// </summary>
public class FormSubmittedEventArgs : EventArgs
{
    public string FeatureId { get; }
    public Dictionary<string, object> FormData { get; }

    public FormSubmittedEventArgs(string featureId, Dictionary<string, object> formData)
    {
        FeatureId = featureId;
        FormData = formData;
    }
}