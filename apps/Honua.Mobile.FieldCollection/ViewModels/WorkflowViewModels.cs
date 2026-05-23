using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Configuration;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Forms;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;
using Microsoft.Maui.Devices.Sensors;
using StorageSyncSession = Honua.Mobile.FieldCollection.Services.Storage.Models.SyncSession;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;

namespace Honua.Mobile.FieldCollection.ViewModels;

public interface IRouteAwareViewModel
{
    void ApplyQueryAttributes(IDictionary<string, object> query);
    Task OnNavigatedToAsync();
}

public sealed class AttributeDisplayItem
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed partial class EditableAttributeItem : ObservableObject
{
    [ObservableProperty]
    private string key = string.Empty;

    [ObservableProperty]
    private string valueText = string.Empty;
}

public sealed partial class EditableChoiceItem : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public string Value { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed partial class EditableRepeatSectionItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    private int entryCount;

    public string SectionId { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public bool CanRemove => EntryCount > 0;

    public ICommand? AddCommand { get; init; }

    public ICommand? RemoveCommand { get; init; }
}

public sealed partial class EditableFormFieldItem : ObservableObject
{
    private bool _suppressValueChanged;

    [ObservableProperty]
    private string textValue = string.Empty;

    [ObservableProperty]
    private bool boolValue;

    [ObservableProperty]
    private DateTime dateValue = DateTime.Today;

    [ObservableProperty]
    private TimeSpan timeValue = DateTime.Now.TimeOfDay;

    [ObservableProperty]
    private FieldChoice? selectedChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string? validationError;

    [ObservableProperty]
    private string valueSummary = string.Empty;

    [ObservableProperty]
    private bool isVisible = true;

    public EditableFormFieldItem(FormField definition)
        : this(new MobileFormFieldBinding(
            new FormSection { SectionId = "default", Label = "Default", Fields = [definition] },
            definition,
            definition.FieldId,
            null))
    {
    }

    public EditableFormFieldItem(MobileFormFieldBinding binding)
    {
        Definition = binding.Field;
        Section = binding.Section;
        ValueKey = binding.ValueKey;
        RepeatIndex = binding.RepeatIndex;
        ControlKind = MobileFormControlSelector.Select(Definition);
        IsReadOnly = Definition.Type == FormFieldType.Calculated || !string.IsNullOrWhiteSpace(Definition.CalculatedExpression);
        Label = BuildLabel(binding);
        HelpText = Definition.HelpText ?? string.Empty;
        foreach (var choice in Definition.Choices)
        {
            var item = new EditableChoiceItem
            {
                Value = choice.Value,
                Label = string.IsNullOrWhiteSpace(choice.Label) ? choice.Value : choice.Label
            };
            item.SelectionChanged += (_, _) => RaiseValueChanged();
            Choices.Add(item);
        }
    }

    public FormField Definition { get; }

    public FormSection Section { get; }

    public string ValueKey { get; }

    public int? RepeatIndex { get; }

    public MobileFormControlKind ControlKind { get; }

    public string Label { get; }

    public string HelpText { get; }

    public ObservableCollection<EditableChoiceItem> Choices { get; } = [];

    public IAsyncRelayCommand<EditableFormFieldItem>? PrimaryActionCommand { get; set; }

    public string PrimaryActionLabel { get; set; } = string.Empty;

    public bool HasPrimaryAction => PrimaryActionCommand != null;

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public bool IsReadOnly { get; }

    public bool IsEditable => !IsReadOnly;

    public bool IsSingleLineText => ControlKind is MobileFormControlKind.SingleLineText or MobileFormControlKind.Barcode;

    public bool IsMultilineText => ControlKind == MobileFormControlKind.MultilineText;

    public bool IsNumeric => ControlKind == MobileFormControlKind.Numeric;

    public bool IsDate => ControlKind == MobileFormControlKind.Date;

    public bool IsDateTime => ControlKind == MobileFormControlKind.DateTime;

    public bool IsYesNo => ControlKind == MobileFormControlKind.YesNo;

    public bool IsSingleChoice => ControlKind is MobileFormControlKind.SingleChoice or MobileFormControlKind.Dropdown;

    public bool IsMultipleChoice => ControlKind == MobileFormControlKind.MultipleChoice;

    public bool IsLocation => ControlKind == MobileFormControlKind.Location;

    public bool IsMedia => ControlKind is MobileFormControlKind.Photo or MobileFormControlKind.File or MobileFormControlKind.Signature;

    public bool IsUnsupported => ControlKind == MobileFormControlKind.Unsupported;

    public event EventHandler? ValueChanged;

    public void SetValue(object? value, IReadOnlyDictionary<string, AttachmentInfo>? attachmentsById = null)
    {
        _suppressValueChanged = true;
        try
        {
            ValidationError = null;

            switch (ControlKind)
            {
                case MobileFormControlKind.YesNo:
                    BoolValue = MobileFormValueConverter.TryGetBoolean(value, out var boolean) && boolean;
                    ValueSummary = BoolValue ? "Yes" : "No";
                    break;
                case MobileFormControlKind.Date:
                    DateValue = MobileFormValueConverter.TryGetDate(value, out var date) ? date : DateTime.Today;
                    ValueSummary = DateValue.ToString("yyyy-MM-dd");
                    break;
                case MobileFormControlKind.DateTime:
                    var dateTime = MobileFormValueConverter.TryGetDateTime(value, out var parsedDateTime)
                        ? parsedDateTime
                        : DateTime.Now;
                    DateValue = dateTime.Date;
                    TimeValue = dateTime.TimeOfDay;
                    ValueSummary = dateTime.ToString("u");
                    break;
                case MobileFormControlKind.SingleChoice:
                case MobileFormControlKind.Dropdown:
                    var selectedValue = MobileFormValueConverter.ToChoiceValues(value).FirstOrDefault();
                    SelectedChoice = Definition.Choices.FirstOrDefault(choice =>
                        string.Equals(choice.Value, selectedValue, StringComparison.Ordinal));
                    ValueSummary = SelectedChoice?.Label ?? SelectedChoice?.Value ?? string.Empty;
                    break;
                case MobileFormControlKind.MultipleChoice:
                    var selected = MobileFormValueConverter
                        .ToChoiceValues(value)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var choice in Choices)
                    {
                        choice.IsSelected = selected.Contains(choice.Value);
                    }

                    ValueSummary = string.Join(", ", Choices.Where(choice => choice.IsSelected).Select(choice => choice.Label));
                    break;
                case MobileFormControlKind.Location:
                    if (MobileFormValueConverter.TryGetLocation(value, out var point))
                    {
                        TextValue = $"{point.Latitude},{point.Longitude},{point.AccuracyMeters?.ToString() ?? string.Empty}";
                        ValueSummary = MobileFormValueConverter.ToDisplayText(Definition, point);
                    }
                    else
                    {
                        TextValue = MobileFormValueConverter.ToDisplayText(Definition, value);
                        ValueSummary = TextValue;
                    }

                    break;
                case MobileFormControlKind.Photo:
                case MobileFormControlKind.File:
                case MobileFormControlKind.Signature:
                    SetMediaValue(MobileFormValueConverter.ToChoiceValues(value), attachmentsById);
                    break;
                default:
                    TextValue = MobileFormValueConverter.ToDisplayText(Definition, value);
                    ValueSummary = TextValue;
                    break;
            }
        }
        finally
        {
            _suppressValueChanged = false;
        }
    }

    public object? ToValue()
    {
        return ControlKind switch
        {
            MobileFormControlKind.YesNo => MobileFormValueConverter.FromBoolean(Definition, BoolValue),
            MobileFormControlKind.Date => MobileFormValueConverter.FromDate(Definition, DateValue),
            MobileFormControlKind.DateTime => MobileFormValueConverter.FromDateTime(Definition, DateValue, TimeValue),
            MobileFormControlKind.SingleChoice or MobileFormControlKind.Dropdown => SelectedChoice?.Value,
            MobileFormControlKind.MultipleChoice => MobileFormValueConverter.FromChoiceValues(
                Definition,
                Choices.Where(choice => choice.IsSelected).Select(choice => choice.Value)),
            MobileFormControlKind.Location => MobileFormValueConverter.NormalizeValue(Definition, TextValue),
            MobileFormControlKind.Photo or MobileFormControlKind.File or MobileFormControlKind.Signature
                => MobileFormValueConverter.ToChoiceValues(TextValue).ToArray(),
            _ => MobileFormValueConverter.FromText(Definition, TextValue)
        };
    }

    public void SetLocation(Location location)
    {
        var value = MobileFormValueConverter.FromLocation(
            location.Latitude,
            location.Longitude,
            location.Accuracy);
        SetValue(value);
        RaiseValueChanged();
    }

    public void AddMediaAttachment(AttachmentInfo attachment)
    {
        var ids = MobileFormValueConverter.ToChoiceValues(TextValue).ToList();
        if (!ids.Contains(attachment.Id, StringComparer.Ordinal))
        {
            ids.Add(attachment.Id);
        }

        SetMediaValue(ids, new Dictionary<string, AttachmentInfo>(StringComparer.Ordinal)
        {
            [attachment.Id] = attachment
        });
        RaiseValueChanged();
    }

    partial void OnTextValueChanged(string value)
    {
        ValueSummary = value;
        RaiseValueChanged();
    }

    partial void OnBoolValueChanged(bool value)
    {
        ValueSummary = value ? "Yes" : "No";
        RaiseValueChanged();
    }

    partial void OnDateValueChanged(DateTime value)
    {
        ValueSummary = ControlKind == MobileFormControlKind.DateTime
            ? value.Date.Add(TimeValue).ToString("u")
            : value.ToString("yyyy-MM-dd");
        RaiseValueChanged();
    }

    partial void OnTimeValueChanged(TimeSpan value)
    {
        ValueSummary = DateValue.Date.Add(value).ToString("u");
        RaiseValueChanged();
    }

    partial void OnSelectedChoiceChanged(FieldChoice? value)
    {
        ValueSummary = value?.Label ?? value?.Value ?? string.Empty;
        RaiseValueChanged();
    }

    private void SetMediaValue(IReadOnlyList<string> attachmentIds, IReadOnlyDictionary<string, AttachmentInfo>? attachmentsById)
    {
        TextValue = string.Join(",", attachmentIds);
        var names = attachmentIds
            .Select(id => attachmentsById != null && attachmentsById.TryGetValue(id, out var attachment)
                ? attachment.FileName
                : id)
            .ToArray();
        ValueSummary = names.Length == 0 ? string.Empty : string.Join(", ", names);
    }

    private void RaiseValueChanged()
    {
        if (!_suppressValueChanged)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string BuildLabel(MobileFormFieldBinding binding)
    {
        var label = binding.Field.Required ? $"{binding.Field.Label} *" : binding.Field.Label;
        if (binding.RepeatIndex is not { } repeatIndex)
        {
            return label;
        }

        var sectionLabel = string.IsNullOrWhiteSpace(binding.Section.Label)
            ? binding.Section.SectionId
            : binding.Section.Label;
        return $"{sectionLabel} {repeatIndex + 1}: {label}";
    }
}

public partial class RecordDetailViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IFeatureService _featureService;
    private readonly IAttachmentService _attachmentService;

    [ObservableProperty]
    private string featureId = string.Empty;

    [ObservableProperty]
    private int layerId;

    [ObservableProperty]
    private Feature? feature;

    [ObservableProperty]
    private string geometrySummary = "No geometry";

    public ObservableCollection<AttributeDisplayItem> Attributes { get; } = [];
    public ObservableCollection<AttachmentInfo> Attachments { get; } = [];

    public RecordDetailViewModel(
        INavigationService navigationService,
        IFeatureService featureService,
        IAttachmentService attachmentService)
        : base(navigationService)
    {
        _featureService = featureService;
        _attachmentService = attachmentService;
        Title = "Record Detail";
    }

    public virtual void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        LayerId = RouteQuery.GetInt(query, "layerId", LayerId == 0 ? 1 : LayerId);
        FeatureId = RouteQuery.GetString(query, "featureId", FeatureId);
    }

    public virtual Task OnNavigatedToAsync() => LoadRecord();

    [RelayCommand]
    protected async Task LoadRecord()
    {
        if (LayerId <= 0 || string.IsNullOrWhiteSpace(FeatureId))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            Feature = await _featureService.GetFeatureAsync(LayerId, FeatureId);
            Attributes.Clear();
            Attachments.Clear();

            if (Feature == null)
            {
                GeometrySummary = "Record not found";
                return;
            }

            foreach (var attribute in Feature.Attributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
            {
                Attributes.Add(new AttributeDisplayItem
                {
                    Key = attribute.Key,
                    Value = FormatValue(attribute.Value)
                });
            }

            GeometrySummary = FormatGeometry(Feature.Geometry);

            foreach (var attachment in await _attachmentService.GetAttachmentsAsync(Feature.Id))
            {
                Attachments.Add(attachment);
            }
        });
    }

    [RelayCommand]
    private async Task EditRecord()
    {
        if (Feature == null)
        {
            return;
        }

        await NavigationService.NavigateToAsync(
            "record-edit",
            new Dictionary<string, object>
            {
                ["layerId"] = Feature.LayerId,
                ["featureId"] = Feature.Id,
                ["isEdit"] = true
            });
    }

    [RelayCommand]
    private async Task DeleteRecord()
    {
        if (Feature == null)
        {
            return;
        }

        var confirmed = await ShowConfirmation(
            "Delete Record",
            $"Delete {Feature.DisplayTitle}?",
            "Delete",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await _featureService.DeleteFeatureAsync(Feature.LayerId, Feature.Id);
            await NavigationService.GoBackAsync();
        });
    }

    [RelayCommand]
    private async Task OpenAttachment(AttachmentInfo attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
        {
            using var _ = await _attachmentService.GetAttachmentAsync(attachment.Id);
            await ShowMessage("Attachment", attachment.FileName);
            return;
        }

        await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(
            new Microsoft.Maui.ApplicationModel.OpenFileRequest(
                attachment.FileName,
                new Microsoft.Maui.Storage.ReadOnlyFile(attachment.LocalPath, attachment.ContentType)));
    }

    private static string FormatGeometry(Geometry? geometry)
    {
        return geometry switch
        {
            FieldPoint point => $"Point {point.Latitude:F6}, {point.Longitude:F6}",
            LineString line => $"Line with {line.Coordinates.Count} vertices",
            Polygon polygon => $"Polygon with {polygon.Coordinates.Sum(ring => ring.Count)} vertices",
            null => "No geometry",
            _ => geometry.Type
        };
    }

    internal static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("u"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("u"),
            bool boolean => boolean ? "Yes" : "No",
            _ => value.ToString() ?? string.Empty
        };
    }
}

public sealed partial class FeatureDetailViewModel : RecordDetailViewModel
{
    public FeatureDetailViewModel(
        INavigationService navigationService,
        IFeatureService featureService,
        IAttachmentService attachmentService)
        : base(navigationService, featureService, attachmentService)
    {
        Title = "Feature Detail";
    }
}

public sealed partial class RecordEditViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IFeatureService _featureService;
    private readonly IAttachmentService _attachmentService;
    private readonly IFormService _formService;
    private readonly IFormDraftService _formDraftService;
    private readonly ILocationService _locationService;
    private bool _isLoadingForm;
    private FormDefinition? _formDefinition;
    private readonly Dictionary<string, int> _repeatSectionCounts = new(StringComparer.Ordinal);

    [ObservableProperty]
    private int layerId = 1;

    [ObservableProperty]
    private string featureId = string.Empty;

    [ObservableProperty]
    private bool isNew = true;

    [ObservableProperty]
    private string pageTitle = "Create Record";

    [ObservableProperty]
    private string geometrySummary = "No geometry";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationSummary))]
    private string validationSummary = string.Empty;

    public bool HasValidationSummary => !string.IsNullOrWhiteSpace(ValidationSummary);

    private FieldPoint? _location;
    private string? _captureSource;
    private DateTime? _capturedAtUtc;
    private double? _gpsAccuracyMeters;
    private Feature? _existingFeature;

    public ObservableCollection<EditableAttributeItem> Attributes { get; } = [];
    public ObservableCollection<EditableFormFieldItem> FormFields { get; } = [];
    public ObservableCollection<EditableRepeatSectionItem> RepeatSections { get; } = [];
    public ObservableCollection<AttachmentInfo> Attachments { get; } = [];

    public RecordEditViewModel(
        INavigationService navigationService,
        IFeatureService featureService,
        IAttachmentService attachmentService,
        IFormService formService,
        IFormDraftService formDraftService,
        ILocationService locationService)
        : base(navigationService)
    {
        _featureService = featureService;
        _attachmentService = attachmentService;
        _formService = formService;
        _formDraftService = formDraftService;
        _locationService = locationService;
        Title = "Create Record";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        LayerId = RouteQuery.GetInt(query, "layerId", LayerId);
        FeatureId = RouteQuery.GetString(query, "featureId", FeatureId);
        IsNew = RouteQuery.GetBool(query, "isNew", string.IsNullOrWhiteSpace(FeatureId));
        _location = RouteQuery.GetValue<FieldPoint>(query, "location");
        _captureSource = RouteQuery.GetString(query, "captureSource", _captureSource ?? string.Empty);
        _capturedAtUtc = RouteQuery.GetDateTime(query, "capturedAtUtc", _capturedAtUtc);
        _gpsAccuracyMeters = RouteQuery.GetDouble(query, "gpsAccuracyMeters", _gpsAccuracyMeters);

        if (RouteQuery.GetBool(query, "isEdit", false))
        {
            IsNew = false;
        }
    }

    public Task OnNavigatedToAsync() => LoadDraft();

    [RelayCommand]
    private async Task LoadDraft()
    {
        await ExecuteAsync(async () =>
        {
            _isLoadingForm = true;
            try
            {
                Attributes.Clear();
                foreach (var field in FormFields)
                {
                    field.ValueChanged -= OnFormFieldValueChanged;
                }

                FormFields.Clear();
                RepeatSections.Clear();
                Attachments.Clear();
                _repeatSectionCounts.Clear();
                ValidationSummary = string.Empty;
                PageTitle = IsNew ? "Create Record" : "Edit Record";
                Title = PageTitle;

                if (IsNew && string.IsNullOrWhiteSpace(FeatureId))
                {
                    FeatureId = Guid.NewGuid().ToString("N");
                }

                if (!IsNew && !string.IsNullOrWhiteSpace(FeatureId))
                {
                    _existingFeature = await _featureService.GetFeatureAsync(LayerId, FeatureId);
                }

                var source = _existingFeature?.Attributes ?? CreateDefaultAttributes();
                var draft = string.IsNullOrWhiteSpace(FeatureId)
                    ? null
                    : await _formDraftService.GetDraftAsync(LayerId, FeatureId);
                if (draft?.Values.Count > 0)
                {
                    source = new Dictionary<string, object?>(source, StringComparer.OrdinalIgnoreCase);
                    foreach (var value in draft.Values)
                    {
                        source[value.Key] = value.Value;
                    }

                    foreach (var repeatCount in draft.RepeatCounts)
                    {
                        _repeatSectionCounts[repeatCount.Key] = repeatCount.Value;
                    }
                }

                if (IsNew)
                {
                    ApplyCaptureMetadata(source);
                }

                foreach (var attribute in source.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Attributes.Add(new EditableAttributeItem
                    {
                        Key = attribute.Key,
                        ValueText = RecordDetailViewModel.FormatValue(attribute.Value)
                    });
                }

                GeometrySummary = FormatGeometry(_existingFeature?.Geometry ?? _location);

                if (!string.IsNullOrWhiteSpace(FeatureId))
                {
                    foreach (var attachment in await _attachmentService.GetAttachmentsAsync(FeatureId))
                    {
                        Attachments.Add(attachment);
                    }
                }

                _formDefinition = await _formService.GetFormDefinitionAsync(LayerId)
                    ?? CreateAdHocFormDefinition(LayerId, source);
                LoadFormFields(_formDefinition, source);
                if (draft?.ValidationErrors.Count > 0)
                {
                    ApplyValidationErrors(draft.ValidationErrors);
                }
            }
            finally
            {
                _isLoadingForm = false;
            }
        });
    }

    [RelayCommand]
    private void AddAttribute()
    {
        var index = Attributes.Count + 1;
        Attributes.Add(new EditableAttributeItem
        {
            Key = $"field_{index}",
            ValueText = string.Empty
        });

        if (_formDefinition != null)
        {
            var section = _formDefinition.Sections.FirstOrDefault(section => !section.Repeatable) ??
                new FormSection { SectionId = "attributes", Label = "Attributes" };
            var field = new FormField
            {
                FieldId = $"field_{index}",
                Label = $"Field {index}",
                Type = FormFieldType.Text
            };
            AddFormField(new MobileFormFieldBinding(section, field, field.FieldId, null), null);
        }
    }

    [RelayCommand]
    private void RemoveAttribute(EditableAttributeItem item)
    {
        Attributes.Remove(item);
    }

    [RelayCommand]
    private async Task AddFileAttachment()
    {
        var picked = await Microsoft.Maui.Storage.FilePicker.Default.PickAsync();
        if (picked == null)
        {
            return;
        }

        await AddAttachmentFromFileResultAsync(picked, AttachmentPayloadKind.File);
    }

    [RelayCommand]
    private async Task AddPhotoAttachment()
    {
        Microsoft.Maui.Storage.FileResult? photo = null;
        if (Microsoft.Maui.Media.MediaPicker.Default.IsCaptureSupported)
        {
            photo = await Microsoft.Maui.Media.MediaPicker.Default.CapturePhotoAsync();
        }

        photo ??= await Microsoft.Maui.Storage.FilePicker.Default.PickAsync(new Microsoft.Maui.Storage.PickOptions
        {
            FileTypes = Microsoft.Maui.Storage.FilePickerFileType.Images
        });
        if (photo == null)
        {
            return;
        }

        await AddAttachmentFromFileResultAsync(photo, AttachmentPayloadKind.Photo);
    }

    [RelayCommand]
    private async Task CaptureFormFieldLocation(EditableFormFieldItem field)
    {
        await ExecuteAsync(async () =>
        {
            var location = await _locationService.GetCurrentLocationAsync();
            if (location == null)
            {
                await ShowError("Location Unavailable", "Unable to determine current location.");
                return;
            }

            field.SetLocation(location);
            await SaveDraftSnapshotAsync();
        });
    }

    [RelayCommand]
    private async Task CaptureFormFieldAttachment(EditableFormFieldItem field)
    {
        Microsoft.Maui.Storage.FileResult? file = null;
        var payloadKind = field.ControlKind switch
        {
            MobileFormControlKind.Photo => AttachmentPayloadKind.Photo,
            MobileFormControlKind.Signature => AttachmentPayloadKind.Signature,
            _ => AttachmentPayloadKind.File
        };

        if (payloadKind == AttachmentPayloadKind.Photo && Microsoft.Maui.Media.MediaPicker.Default.IsCaptureSupported)
        {
            file = await Microsoft.Maui.Media.MediaPicker.Default.CapturePhotoAsync();
        }

        file ??= payloadKind switch
        {
            AttachmentPayloadKind.Photo => await Microsoft.Maui.Storage.FilePicker.Default.PickAsync(
                new Microsoft.Maui.Storage.PickOptions { FileTypes = Microsoft.Maui.Storage.FilePickerFileType.Images }),
            _ => await Microsoft.Maui.Storage.FilePicker.Default.PickAsync()
        };

        if (file == null)
        {
            return;
        }

        await AddAttachmentFromFileResultAsync(file, payloadKind, field);
    }

    [RelayCommand]
    private async Task CaptureBarcode(EditableFormFieldItem field)
    {
        var value = await NavigationService.DisplayPromptAsync(
            "Barcode / QR",
            "Enter scanned code",
            placeholder: "Code",
            initialValue: field.TextValue);
        if (value == null)
        {
            return;
        }

        field.TextValue = value;
        await SaveDraftSnapshotAsync();
    }

    [RelayCommand]
    private async Task AddRepeatEntry(EditableRepeatSectionItem section)
    {
        if (_formDefinition == null || string.IsNullOrWhiteSpace(section.SectionId))
        {
            return;
        }

        var values = BuildFormValues(includeHidden: true);
        _repeatSectionCounts[section.SectionId] = section.EntryCount + 1;
        ReloadFormFields(values);
        await SaveDraftSnapshotAsync();
    }

    [RelayCommand]
    private async Task RemoveRepeatEntry(EditableRepeatSectionItem section)
    {
        if (_formDefinition == null || string.IsNullOrWhiteSpace(section.SectionId) || section.EntryCount <= 0)
        {
            return;
        }

        var values = BuildFormValues(includeHidden: true);
        var removedIndex = section.EntryCount - 1;
        foreach (var key in values.Keys.ToArray())
        {
            if (MobileFormRepeatKey.TryParse(key, out var sectionId, out var repeatIndex, out _) &&
                repeatIndex == removedIndex &&
                string.Equals(sectionId, section.SectionId, StringComparison.Ordinal))
            {
                values.Remove(key);
            }
        }

        _repeatSectionCounts[section.SectionId] = removedIndex;
        ReloadFormFields(values);
        await SaveDraftSnapshotAsync();
    }

    [RelayCommand]
    private async Task RemoveAttachment(AttachmentInfo attachment)
    {
        await ExecuteAsync(async () =>
        {
            await _attachmentService.DeleteAttachmentAsync(attachment.Id);
            Attachments.Remove(attachment);
        });
    }

    private async Task AddAttachmentFromFileResultAsync(
        Microsoft.Maui.Storage.FileResult file,
        AttachmentPayloadKind payloadKind,
        EditableFormFieldItem? ownerField = null)
    {
        await ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(FeatureId))
            {
                FeatureId = Guid.NewGuid().ToString("N");
            }

            await using var stream = await file.OpenReadAsync();
            var attachment = await _attachmentService.SaveAttachmentAsync(
                LayerId,
                FeatureId,
                stream,
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                payloadKind,
                ownerField?.Definition.FieldId);
            Attachments.Add(attachment);
            ownerField?.AddMediaAttachment(attachment);
            await SaveDraftSnapshotAsync();
        });
    }

    [RelayCommand]
    private async Task SaveRecord()
    {
        await ExecuteAsync(async () =>
        {
            var feature = _existingFeature ?? new Feature
            {
                Id = string.IsNullOrWhiteSpace(FeatureId) ? Guid.NewGuid().ToString("N") : FeatureId,
                LayerId = LayerId,
                Geometry = _location
            };

            feature.LayerId = LayerId;
            var values = BuildFormValues();
            if (_formDefinition != null)
            {
                var formData = BuildFormData(values);
                var valid = await _formService.ValidateFormAsync(formData, _formDefinition);
                values = formData.Values;
                ApplyCalculatedValues(values);
                ApplyValidationErrors(formData.ValidationErrors);
                if (!valid)
                {
                    ValidationSummary = $"Fix {formData.ValidationErrors.Count} field error(s) before saving.";
                    return;
                }
            }

            feature.Attributes = values;

            if (IsNew)
            {
                await _featureService.CreateFeatureAsync(LayerId, feature);
            }
            else
            {
                await _featureService.UpdateFeatureAsync(LayerId, feature);
            }

            FeatureId = feature.Id;
            IsNew = false;
            await _formDraftService.DeleteDraftAsync(LayerId, FeatureId);
            await NavigationService.NavigateToAsync(
                "record-detail",
                new Dictionary<string, object>
                {
                    ["layerId"] = LayerId,
                    ["featureId"] = FeatureId
                });
        });
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (IsNew && !string.IsNullOrWhiteSpace(FeatureId))
        {
            foreach (var attachment in Attachments.ToList())
            {
                await _attachmentService.DeleteAttachmentAsync(attachment.Id);
            }

            await _formDraftService.DeleteDraftAsync(LayerId, FeatureId);
        }

        await NavigationService.GoBackAsync();
    }

    private void LoadFormFields(FormDefinition formDefinition, IReadOnlyDictionary<string, object?> values)
    {
        var seededValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var section in formDefinition.Sections.Where(section => section.Repeatable))
        {
            if (!_repeatSectionCounts.TryGetValue(section.SectionId, out var repeatCount))
            {
                continue;
            }

            for (var repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
            {
                foreach (var field in section.Fields)
                {
                    seededValues.TryAdd(MobileFormRepeatKey.ForField(section, repeatIndex, field), null);
                }
            }
        }

        var resolvedValues = MobileFormRuleRuntime.ApplyCalculatedValues(
            formDefinition,
            MobileFormRuleRuntime.ApplyDefaultValues(formDefinition, seededValues));
        var attachmentsById = Attachments.ToDictionary(attachment => attachment.Id, StringComparer.Ordinal);

        foreach (var section in formDefinition.Sections)
        {
            if (!section.Repeatable)
            {
                foreach (var field in section.Fields)
                {
                    resolvedValues.TryGetValue(field.FieldId, out var value);
                    AddFormField(new MobileFormFieldBinding(section, field, field.FieldId, null), value, attachmentsById);
                }

                continue;
            }

            var repeatCount = _repeatSectionCounts.TryGetValue(section.SectionId, out var configuredCount)
                ? Math.Max(0, configuredCount)
                : MobileFormRepeatKey.GetRepeatCount(section, resolvedValues, defaultCount: 1);
            _repeatSectionCounts[section.SectionId] = repeatCount;
            RepeatSections.Add(new EditableRepeatSectionItem
            {
                SectionId = section.SectionId,
                Label = string.IsNullOrWhiteSpace(section.Label) ? section.SectionId : section.Label,
                EntryCount = repeatCount,
                AddCommand = AddRepeatEntryCommand,
                RemoveCommand = RemoveRepeatEntryCommand
            });

            for (var repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
            {
                foreach (var field in section.Fields)
                {
                    var valueKey = MobileFormRepeatKey.ForField(section, repeatIndex, field);
                    resolvedValues.TryGetValue(valueKey, out var value);
                    AddFormField(new MobileFormFieldBinding(section, field, valueKey, repeatIndex), value, attachmentsById);
                }
            }
        }

        RefreshFormRules();
    }

    private void AddFormField(
        MobileFormFieldBinding binding,
        object? value,
        IReadOnlyDictionary<string, AttachmentInfo>? attachmentsById = null)
    {
        var field = binding.Field;
        var item = new EditableFormFieldItem(binding)
        {
            PrimaryActionCommand = field.Type switch
            {
                FormFieldType.Location => CaptureFormFieldLocationCommand,
                FormFieldType.Photo or FormFieldType.File or FormFieldType.Signature => CaptureFormFieldAttachmentCommand,
                FormFieldType.Barcode => CaptureBarcodeCommand,
                _ => null
            },
            PrimaryActionLabel = field.Type switch
            {
                FormFieldType.Location => "Use current location",
                FormFieldType.Photo => "Add photo",
                FormFieldType.Signature => "Add signature",
                FormFieldType.File => "Add file",
                FormFieldType.Barcode => "Scan or enter code",
                _ => string.Empty
            }
        };
        item.SetValue(value, attachmentsById);
        item.ValueChanged += OnFormFieldValueChanged;
        FormFields.Add(item);
    }

    private void ReloadFormFields(IReadOnlyDictionary<string, object?> values)
    {
        if (_formDefinition == null)
        {
            return;
        }

        _isLoadingForm = true;
        try
        {
            foreach (var field in FormFields)
            {
                field.ValueChanged -= OnFormFieldValueChanged;
            }

            FormFields.Clear();
            RepeatSections.Clear();
            LoadFormFields(_formDefinition, values);
        }
        finally
        {
            _isLoadingForm = false;
        }
    }

    private void OnFormFieldValueChanged(object? sender, EventArgs e)
    {
        if (_isLoadingForm)
        {
            return;
        }

        ValidationSummary = string.Empty;
        if (sender is EditableFormFieldItem item)
        {
            item.ValidationError = null;
        }

        RefreshFormRules();
        _ = SaveDraftSnapshotAsync();
    }

    private async Task SaveDraftSnapshotAsync()
    {
        if (_isLoadingForm || string.IsNullOrWhiteSpace(FeatureId))
        {
            return;
        }

        await _formDraftService.SaveDraftAsync(new FormDraftSnapshot
        {
            LayerId = LayerId,
            FeatureId = FeatureId,
            FormId = _formDefinition?.FormId,
            Values = BuildFormValues(includeHidden: true),
            ValidationErrors = FormFields
                .Where(field => field.HasValidationError)
                .ToDictionary(field => field.ValueKey, field => field.ValidationError!, StringComparer.OrdinalIgnoreCase),
            RepeatCounts = new Dictionary<string, int>(_repeatSectionCounts, StringComparer.Ordinal)
        });
    }

    private Dictionary<string, object?> BuildFormValues(bool includeHidden = true)
    {
        if (FormFields.Count == 0)
        {
            return Attributes
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
                .GroupBy(attribute => attribute.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => ParseAttributeValue(group.Last().ValueText),
                    StringComparer.OrdinalIgnoreCase);
        }

        return FormFields
            .Where(field => !string.IsNullOrWhiteSpace(field.ValueKey) && (includeHidden || field.IsVisible))
            .ToDictionary(
                field => field.ValueKey,
                field => field.ToValue(),
                StringComparer.OrdinalIgnoreCase);
    }

    private FormData BuildFormData(Dictionary<string, object?> values)
    {
        var attachmentsById = Attachments.ToDictionary(attachment => attachment.Id, StringComparer.Ordinal);
        var location = FormFields
            .Where(field => field.IsVisible && field.Definition.Type == FormFieldType.Location)
            .Select(field => values.TryGetValue(field.ValueKey, out var value) &&
                MobileFormValueConverter.TryGetLocation(value, out var point)
                    ? point
                    : (FieldGeoPoint?)null)
            .FirstOrDefault(point => point != null);

        return new FormData
        {
            LayerId = LayerId,
            FeatureId = FeatureId,
            Values = values,
            Media = BuildMediaAttachments(values, attachmentsById),
            Location = location,
            CreatedAt = _existingFeature?.CreatedAt ?? DateTime.UtcNow
        };
    }

    private List<FieldMediaAttachment> BuildMediaAttachments(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, AttachmentInfo> attachmentsById)
    {
        var media = new List<FieldMediaAttachment>();
        foreach (var field in FormFields.Where(field => MobileFormValueConverter.IsMediaField(field.Definition)))
        {
            if (!values.TryGetValue(field.ValueKey, out var rawValue))
            {
                continue;
            }

            foreach (var attachmentId in MobileFormValueConverter.ToChoiceValues(rawValue))
            {
                if (!attachmentsById.TryGetValue(attachmentId, out var attachment))
                {
                    continue;
                }

                media.Add(new FieldMediaAttachment
                {
                    AttachmentId = attachment.Id,
                    FieldId = field.ValueKey,
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    SizeBytes = attachment.SizeBytes,
                    CapturedAtUtc = new DateTimeOffset(attachment.CreatedAt == default ? DateTime.UtcNow : attachment.CreatedAt),
                    MediaType = MobileFormValueConverter.ToSdkMediaType(field.Definition.Type, attachment.PayloadKind)
                });
            }
        }

        return media;
    }

    private void ApplyValidationErrors(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var field in FormFields)
        {
            field.ValidationError = errors.TryGetValue(field.ValueKey, out var error)
                ? error
                : null;
        }

        ValidationSummary = errors.Count == 0
            ? string.Empty
            : $"Fix {errors.Count} field error(s) before saving.";
    }

    private void RefreshFormRules()
    {
        if (_formDefinition == null || FormFields.Count == 0)
        {
            return;
        }

        var values = MobileFormRuleRuntime.ApplyCalculatedValues(_formDefinition, BuildFormValues(includeHidden: true));
        ApplyCalculatedValues(values);
        ApplyVisibility(values);
    }

    private void ApplyCalculatedValues(IReadOnlyDictionary<string, object?> values)
    {
        foreach (var field in FormFields.Where(field =>
            field.Definition.Type == FormFieldType.Calculated ||
            !string.IsNullOrWhiteSpace(field.Definition.CalculatedExpression)))
        {
            if (values.TryGetValue(field.ValueKey, out var value))
            {
                field.SetValue(value);
            }
        }
    }

    private void ApplyVisibility(IReadOnlyDictionary<string, object?> values)
    {
        if (_formDefinition == null)
        {
            return;
        }

        foreach (var field in FormFields)
        {
            field.IsVisible = MobileFormRuleRuntime.IsFieldVisible(
                _formDefinition,
                field.Section,
                field.Definition,
                values,
                field.RepeatIndex);
            if (!field.IsVisible)
            {
                field.ValidationError = null;
            }
        }
    }

    private static FormDefinition CreateAdHocFormDefinition(int layerId, IReadOnlyDictionary<string, object?> source)
    {
        return new FormDefinition
        {
            FormId = $"layer-{layerId}:ad-hoc",
            Name = $"Layer {layerId}",
            Sections =
            [
                new FormSection
                {
                    SectionId = "attributes",
                    Label = "Attributes",
                    Fields = source
                        .OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(attribute => new FormField
                        {
                            FieldId = attribute.Key,
                            Label = attribute.Key,
                            Type = InferAdHocFieldType(attribute.Value)
                        })
                        .ToList()
                }
            ]
        };
    }

    private static FormFieldType InferAdHocFieldType(object? value)
    {
        return value switch
        {
            bool => FormFieldType.YesNo,
            byte or short or int or long or float or double or decimal => FormFieldType.Numeric,
            DateTime or DateTimeOffset => FormFieldType.DateTime,
            _ => FormFieldType.Text
        };
    }

    private static Dictionary<string, object?> CreateDefaultAttributes() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = string.Empty,
            ["status"] = "new",
            ["notes"] = string.Empty
        };

    private void ApplyCaptureMetadata(IDictionary<string, object?> values)
    {
        if (!string.IsNullOrWhiteSpace(_captureSource))
        {
            values.TryAdd("capture_source", _captureSource);
        }

        if (_capturedAtUtc.HasValue)
        {
            values.TryAdd("captured_at_utc", _capturedAtUtc.Value);
        }

        if (_gpsAccuracyMeters.HasValue)
        {
            values.TryAdd("gps_accuracy_m", _gpsAccuracyMeters.Value);
        }

        if (_location != null)
        {
            values.TryAdd("capture_latitude", _location.Latitude);
            values.TryAdd("capture_longitude", _location.Longitude);
        }
    }

    private static object? ParseAttributeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        if (long.TryParse(value, out var integer))
        {
            return integer;
        }

        if (double.TryParse(value, out var number))
        {
            return number;
        }

        return value;
    }

    private static string FormatGeometry(Geometry? geometry)
    {
        return geometry switch
        {
            FieldPoint point => $"Point {point.Latitude:F6}, {point.Longitude:F6}",
            null => "No geometry",
            _ => geometry.Type
        };
    }
}

public sealed partial class AuthenticationViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private string serverUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Not signed in";

    [ObservableProperty]
    private bool isAuthenticated;

    public AuthenticationViewModel(INavigationService navigationService, IAuthenticationService authService)
        : base(navigationService)
    {
        _authService = authService;
        Title = "Authentication";
        RefreshState();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        RefreshState();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ValidateConnection()
    {
        await ExecuteAsync(async () =>
        {
            var valid = await _authService.ValidateConnectionAsync(ServerUrl, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey);
            StatusMessage = valid ? "Connection validated" : "Connection failed";
        });
    }

    [RelayCommand]
    private async Task SignIn()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _authService.AuthenticateAsync(ServerUrl, ApiKey);
            StatusMessage = result.IsSuccess ? $"Signed in as {result.UserName}" : result.ErrorMessage ?? "Sign-in failed";
            RefreshState();
        });
    }

    [RelayCommand]
    private async Task Logout()
    {
        await _authService.LogoutAsync();
        RefreshState();
    }

    private void RefreshState()
    {
        ServerUrl = _authService.ServerUrl ?? ServerUrl;
        ApiKey = _authService.ApiKey ?? ApiKey;
        IsAuthenticated = _authService.IsAuthenticated;
        StatusMessage = IsAuthenticated
            ? $"Signed in as {_authService.CurrentUserName ?? _authService.CurrentUserId ?? "current user"}"
            : "Not signed in";
    }
}

public sealed partial class DiagnosticsViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly DiagnosticService _diagnosticService;

    [ObservableProperty]
    private string summary = "Diagnostics not loaded";

    [ObservableProperty]
    private string exportPath = string.Empty;

    public DiagnosticsViewModel(INavigationService navigationService, DiagnosticService diagnosticService)
        : base(navigationService)
    {
        _diagnosticService = diagnosticService;
        Title = "Diagnostics";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync() => LoadDiagnostics();

    [RelayCommand]
    private async Task LoadDiagnostics()
    {
        await ExecuteAsync(UpdateSummaryAsync);
    }

    [RelayCommand]
    private async Task ExportDiagnostics()
    {
        await ExecuteAsync(async () =>
        {
            ExportPath = await _diagnosticService.ExportDiagnosticsAsync();
        });
    }

    [RelayCommand]
    private async Task CompactDatabase()
    {
        await ExecuteAsync(async () =>
        {
            var compacted = await _diagnosticService.CompactDatabaseAsync();
            Summary = compacted ? "Database compacted" : "Database compaction was not needed";
            await UpdateSummaryAsync();
        });
    }

    private async Task UpdateSummaryAsync()
    {
        var report = await _diagnosticService.GenerateDiagnosticReportAsync();
        Summary =
            $"App {report.AppVersion}\n" +
            $"Platform {report.System.Platform}\n" +
            $"Online {report.Connectivity.IsConnected}\n" +
            $"Remote sync configured {report.Sync.IsRemoteSyncConfigured}\n" +
            $"Pending changes {report.Sync.PendingChanges}\n" +
            $"Pending attachments {report.OfflineCache.Operations.AttachmentPendingCount}\n" +
            $"Attachment failures {report.OfflineCache.Operations.AttachmentFailedCount}\n" +
            $"Conflicts {report.Sync.ConflictCount}\n" +
            $"Database {report.Database.DatabaseSize}\n" +
            $"Offline cache {report.OfflineCache.PackageSizeDisplay}";
    }
}

public sealed partial class LayerSettingsViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IFeatureService _featureService;

    [ObservableProperty]
    private int layerId = 1;

    [ObservableProperty]
    private string layerName = "Layer 1";

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private int featureCount;

    [ObservableProperty]
    private int pendingCount;

    public LayerSettingsViewModel(INavigationService navigationService, IFeatureService featureService)
        : base(navigationService)
    {
        _featureService = featureService;
        Title = "Layer Settings";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        LayerId = RouteQuery.GetInt(query, "layerId", LayerId);
        LayerName = $"Layer {LayerId}";
    }

    public Task OnNavigatedToAsync() => LoadLayerState();

    [RelayCommand]
    private async Task LoadLayerState()
    {
        await ExecuteAsync(async () =>
        {
            var features = (await _featureService.GetFeaturesAsync(LayerId)).ToList();
            FeatureCount = features.Count;
            PendingCount = features.Count(feature => feature.IsPendingSync);
        });
    }
}

public sealed partial class ConflictResolutionViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly ISyncService _syncService;

    [ObservableProperty]
    private string conflictId = string.Empty;

    [ObservableProperty]
    private ConflictInfo? conflict;

    [ObservableProperty]
    private string localVersion = string.Empty;

    [ObservableProperty]
    private string serverVersion = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public ConflictResolutionViewModel(INavigationService navigationService, ISyncService syncService)
        : base(navigationService)
    {
        _syncService = syncService;
        Title = "Conflict Resolution";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ConflictId = RouteQuery.GetString(query, "conflictId", ConflictId);
    }

    public Task OnNavigatedToAsync() => LoadConflict();

    [RelayCommand]
    private async Task LoadConflict()
    {
        await ExecuteAsync(async () =>
        {
            Conflict = (await _syncService.GetConflictsAsync())
                .FirstOrDefault(conflict => conflict.Id == ConflictId);

            LocalVersion = Conflict?.RedactedLocalVersion ?? Conflict?.LocalVersion?.ToString() ?? string.Empty;
            ServerVersion = Conflict?.RedactedServerVersion ?? Conflict?.ServerVersion?.ToString() ?? string.Empty;
            StatusMessage = Conflict == null ? "Conflict not found" : Conflict.ConflictDescription;
        });
    }

    [RelayCommand]
    private Task AcceptLocal() => Resolve(ConflictResolution.AcceptLocal);

    [RelayCommand]
    private Task AcceptServer() => Resolve(ConflictResolution.AcceptServer);

    [RelayCommand]
    private async Task Defer()
    {
        if (string.IsNullOrWhiteSpace(ConflictId))
        {
            return;
        }

        var success = await _syncService.DeferConflictAsync(ConflictId);
        StatusMessage = success ? "Conflict deferred for manual review" : "Conflict defer failed";
        if (success)
        {
            await NavigationService.GoBackAsync();
        }
    }

    private async Task Resolve(ConflictResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(ConflictId))
        {
            return;
        }

        var success = await _syncService.ResolveConflictAsync(ConflictId, resolution);
        StatusMessage = success ? "Conflict resolved" : "Conflict resolution failed";
        if (success)
        {
            await NavigationService.GoBackAsync();
        }
    }
}

public sealed class SyncHistoryRow
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int ChangesPulled { get; init; }
    public int ChangesPushed { get; init; }
    public int ConflictsDetected { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string Summary => $"{Status}: pulled {ChangesPulled}, pushed {ChangesPushed}, conflicts {ConflictsDetected}";
}

public sealed partial class SyncHistoryViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly DatabaseService _databaseService;

    public ObservableCollection<SyncHistoryRow> Sessions { get; } = [];

    public SyncHistoryViewModel(INavigationService navigationService, DatabaseService databaseService)
        : base(navigationService)
    {
        _databaseService = databaseService;
        Title = "Sync History";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync() => LoadHistory();

    [RelayCommand]
    private async Task LoadHistory()
    {
        await ExecuteAsync(async () =>
        {
            var storage = await _databaseService.GetStorageServiceAsync();
            var sessions = await storage.GetSyncSessionsAsync();
            Sessions.Clear();

            foreach (var session in sessions)
            {
                Sessions.Add(MapSession(session));
            }
        });
    }

    private static SyncHistoryRow MapSession(StorageSyncSession session) =>
        new()
        {
            Id = session.Id,
            Status = session.Status.ToString(),
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            ChangesPulled = session.ChangesPulled,
            ChangesPushed = session.ChangesPushed,
            ConflictsDetected = session.ConflictsDetected,
            ErrorMessage = session.ErrorMessage ?? string.Empty
        };
}

public sealed partial class ServerConfigViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private string serverUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    public ServerConfigViewModel(INavigationService navigationService, IAuthenticationService authService)
        : base(navigationService)
    {
        _authService = authService;
        Title = "Server Configuration";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        ServerUrl = _authService.ServerUrl ?? ServerUrl;
        ApiKey = _authService.ApiKey ?? ApiKey;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Save()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _authService.AuthenticateAsync(ServerUrl, ApiKey);
            ValidationMessage = result.IsSuccess ? "Server configuration saved" : result.ErrorMessage ?? "Server configuration failed";
        });
    }

    [RelayCommand]
    private async Task Validate()
    {
        await ExecuteAsync(async () =>
        {
            ValidationMessage = await _authService.ValidateConnectionAsync(ServerUrl, ApiKey)
                ? "Server reachable"
                : "Server unreachable";
        });
    }
}

public sealed partial class UserProfileViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private string userName = "Not signed in";

    [ObservableProperty]
    private string serverUrl = string.Empty;

    public UserProfileViewModel(INavigationService navigationService, IAuthenticationService authService)
        : base(navigationService)
    {
        _authService = authService;
        Title = "User Profile";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        UserName = _authService.CurrentUserName ?? _authService.CurrentUserId ?? "Not signed in";
        ServerUrl = _authService.ServerUrl ?? string.Empty;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Logout()
    {
        await _authService.LogoutAsync();
        await OnNavigatedToAsync();
    }
}

public sealed partial class AboutViewModel : BaseViewModel, IRouteAwareViewModel
{
    private readonly MobileBuildConfiguration _buildConfiguration;

    [ObservableProperty]
    private string summary = string.Empty;

    public AboutViewModel(INavigationService navigationService, MobileBuildConfiguration buildConfiguration)
        : base(navigationService)
    {
        _buildConfiguration = buildConfiguration;
        Title = "About";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
    }

    public Task OnNavigatedToAsync()
    {
        Summary =
            $"Honua Field Collection\n" +
            $"Environment: {_buildConfiguration.Metadata.BuildEnvironment}\n" +
            $"Build: {_buildConfiguration.Metadata.VersionDisplay}\n" +
            $"Commit: {_buildConfiguration.Metadata.CommitSha}";
        return Task.CompletedTask;
    }
}

internal static class RouteQuery
{
    public static string GetString(IDictionary<string, object> query, string key, string fallback)
    {
        return query.TryGetValue(key, out var value) ? value?.ToString() ?? fallback : fallback;
    }

    public static int GetInt(IDictionary<string, object> query, string key, int fallback)
    {
        if (!query.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            int integer => integer,
            long integer => checked((int)integer),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    public static bool GetBool(IDictionary<string, object> query, string key, bool fallback)
    {
        if (!query.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    public static double? GetDouble(IDictionary<string, object> query, string key, double? fallback)
    {
        if (!query.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            double number => number,
            float number => number,
            decimal number => (double)number,
            int number => number,
            long number => number,
            string text when double.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    public static DateTime? GetDateTime(IDictionary<string, object> query, string key, DateTime? fallback)
    {
        if (!query.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            string text when DateTime.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    public static T? GetValue<T>(IDictionary<string, object> query, string key) where T : class
    {
        return query.TryGetValue(key, out var value) ? value as T : null;
    }
}
