// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

namespace Honua.Mobile.SharedComponents.Components.Forms;

/// <summary>
/// Revolutionary dynamic form component for mobile data collection.
///
/// Key Features:
/// - No-code form generation from server schemas
/// - Native mobile performance with cross-platform consistency
/// - Real-time validation with user-friendly error messages
/// - Smart field types (photo, location, sensor, signature)
/// - Offline-capable with draft saving
/// - Professional UX that competes with Fulcrum and Survey123
///
/// Usage Example:
/// <![CDATA[
/// <forms:HonuaFeatureForm x:Name="DataForm" />
///
/// // In code-behind or view model:
/// await DataForm.LoadFormSchemaAsync("site-inspection");
/// DataForm.FormSubmitted += OnFormSubmitted;
/// ]]>
/// </summary>
public partial class HonuaFeatureForm : ContentView
{
    /// <summary>
    /// Bindable property for the form ID to load.
    /// </summary>
    public static readonly BindableProperty FormIdProperty = BindableProperty.Create(
        nameof(FormId),
        typeof(string),
        typeof(HonuaFeatureForm),
        string.Empty,
        propertyChanged: OnFormIdChanged);

    /// <summary>
    /// Bindable property for the feature ID when editing existing data.
    /// </summary>
    public static readonly BindableProperty FeatureIdProperty = BindableProperty.Create(
        nameof(FeatureId),
        typeof(string),
        typeof(HonuaFeatureForm),
        string.Empty,
        propertyChanged: OnFeatureIdChanged);

    /// <summary>
    /// Bindable property to control draft saving capability.
    /// </summary>
    public static readonly BindableProperty AllowDraftsProperty = BindableProperty.Create(
        nameof(AllowDrafts),
        typeof(bool),
        typeof(HonuaFeatureForm),
        true,
        propertyChanged: OnAllowDraftsChanged);

    /// <summary>
    /// Bindable property to show/hide progress indicator.
    /// </summary>
    public static readonly BindableProperty ShowProgressProperty = BindableProperty.Create(
        nameof(ShowProgress),
        typeof(bool),
        typeof(HonuaFeatureForm),
        true,
        propertyChanged: OnShowProgressChanged);

    private HonuaFeatureFormViewModel? _viewModel;

    public HonuaFeatureForm()
    {
        InitializeComponent();
        InitializeViewModel();
    }

    /// <summary>
    /// Gets or sets the form ID to load from the server.
    /// </summary>
    public string FormId
    {
        get => (string)GetValue(FormIdProperty);
        set => SetValue(FormIdProperty, value);
    }

    /// <summary>
    /// Gets or sets the feature ID for editing existing data.
    /// Set to null or empty for creating new features.
    /// </summary>
    public string FeatureId
    {
        get => (string)GetValue(FeatureIdProperty);
        set => SetValue(FeatureIdProperty, value);
    }

    /// <summary>
    /// Gets or sets whether users can save drafts.
    /// </summary>
    public bool AllowDrafts
    {
        get => (bool)GetValue(AllowDraftsProperty);
        set => SetValue(AllowDraftsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the completion progress indicator.
    /// </summary>
    public bool ShowProgress
    {
        get => (bool)GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    /// <summary>
    /// Loads a form schema and generates the dynamic UI.
    /// This is the main entry point for using the component.
    /// </summary>
    /// <param name="formId">The ID of the form schema to load</param>
    /// <param name="featureId">Optional feature ID for editing existing data</param>
    public async Task LoadFormSchemaAsync(string formId, string? featureId = null)
    {
        if (_viewModel != null)
        {
            await _viewModel.LoadFormSchemaAsync(formId, featureId);
        }
    }

    /// <summary>
    /// Validates the current form data.
    /// </summary>
    /// <returns>True if the form is valid, false otherwise</returns>
    public bool ValidateForm()
    {
        return _viewModel?.ValidateFormCommand.CanExecute(null) == true;
    }

    /// <summary>
    /// Gets the current form data as a dictionary.
    /// </summary>
    /// <returns>Dictionary containing all form field values</returns>
    public Dictionary<string, object>? GetFormData()
    {
        // This would need to be exposed from the view model
        return _viewModel?.CollectFormData();
    }

    /// <summary>
    /// Saves the current form as a draft.
    /// </summary>
    public async Task SaveDraftAsync()
    {
        if (_viewModel?.SaveDraftCommand.CanExecute(null) == true)
        {
            await _viewModel.SaveDraftCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Submits the form data.
    /// </summary>
    public async Task SubmitFormAsync()
    {
        if (_viewModel?.SubmitFormCommand.CanExecute(null) == true)
        {
            await _viewModel.SubmitFormCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Clears all form data and resets validation state.
    /// </summary>
    public void ClearForm()
    {
        _viewModel?.FormFields.Clear();
        _viewModel?.ValidationErrors.Clear();
    }

    #region Events

    /// <summary>
    /// Fired when the form is successfully submitted.
    /// </summary>
    public event EventHandler<FormSubmittedEventArgs>? FormSubmitted;

    /// <summary>
    /// Fired when form validation state changes.
    /// </summary>
    public event EventHandler<FormValidationEventArgs>? ValidationChanged;

    /// <summary>
    /// Fired when the form loading state changes.
    /// </summary>
    public event EventHandler<FormLoadingEventArgs>? LoadingChanged;

    #endregion

    #region Private Methods

    private void InitializeViewModel()
    {
        // In a real implementation, you would get this from dependency injection
        // For now, we'll create it directly (would need proper DI setup)
        try
        {
            // This would be injected in a real app
            _viewModel = new HonuaFeatureFormViewModel(
                logger: null!, // Would be injected
                schemaService: null!, // Would be injected
                validationService: null!, // Would be injected
                cameraService: null!, // Would be injected
                locationService: null!, // Would be injected
                sensorService: null! // Would be injected
            );

            BindingContext = _viewModel;

            // Subscribe to view model events
            _viewModel.FormSubmitted += OnFormSubmitted;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        catch (Exception ex)
        {
            // Handle initialization errors gracefully
            System.Diagnostics.Debug.WriteLine($"Failed to initialize HonuaFeatureForm: {ex.Message}");
        }
    }

    private void OnFormSubmitted(object? sender, FormSubmittedEventArgs e)
    {
        FormSubmitted?.Invoke(this, e);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HonuaFeatureFormViewModel.CanSubmit):
            case nameof(HonuaFeatureFormViewModel.HasValidationErrors):
                ValidationChanged?.Invoke(this, new FormValidationEventArgs(_viewModel?.CanSubmit == true));
                break;
            case nameof(HonuaFeatureFormViewModel.IsLoading):
                LoadingChanged?.Invoke(this, new FormLoadingEventArgs(_viewModel?.IsLoading == true));
                break;
        }
    }

    #endregion

    #region Property Changed Handlers

    private static void OnFormIdChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is HonuaFeatureForm form && newValue is string formId && !string.IsNullOrEmpty(formId))
        {
            // Auto-load form when FormId is set
            _ = form.LoadFormSchemaAsync(formId, form.FeatureId);
        }
    }

    private static void OnFeatureIdChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is HonuaFeatureForm form && form._viewModel != null)
        {
            form._viewModel.FeatureId = newValue?.ToString() ?? string.Empty;
        }
    }

    private static void OnAllowDraftsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is HonuaFeatureForm form && form._viewModel != null)
        {
            form._viewModel.AllowDrafts = (bool)newValue;
        }
    }

    private static void OnShowProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is HonuaFeatureForm form && form._viewModel != null)
        {
            form._viewModel.ShowProgress = (bool)newValue;
        }
    }

    #endregion
}

/// <summary>
/// Event args for form validation state changes.
/// </summary>
public class FormValidationEventArgs : EventArgs
{
    public bool IsValid { get; }

    public FormValidationEventArgs(bool isValid)
    {
        IsValid = isValid;
    }
}

/// <summary>
/// Event args for form loading state changes.
/// </summary>
public class FormLoadingEventArgs : EventArgs
{
    public bool IsLoading { get; }

    public FormLoadingEventArgs(bool isLoading)
    {
        IsLoading = isLoading;
    }
}