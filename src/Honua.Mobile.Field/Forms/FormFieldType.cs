namespace Honua.Mobile.Field.Forms;

/// <summary>
/// Data types supported by form fields.
/// </summary>
public enum FormFieldType
{
    /// <summary>Free-form text input.</summary>
    Text,
    /// <summary>Numeric input with optional min/max validation.</summary>
    Numeric,
    /// <summary>Date-only input.</summary>
    Date,
    /// <summary>Time-only input.</summary>
    Time,
    /// <summary>Boolean yes/no toggle.</summary>
    YesNo,
    /// <summary>Single selection from a predefined list.</summary>
    SingleChoice,
    /// <summary>Multiple selections from a predefined list.</summary>
    MultipleChoice,
    /// <summary>Hierarchical classification picker.</summary>
    Classification,
    /// <summary>Postal address input.</summary>
    Address,
    /// <summary>URL input validated as an absolute URI.</summary>
    Hyperlink,
    /// <summary>Link to another record.</summary>
    RecordLink,
    /// <summary>Auto-computed field driven by a calculated expression.</summary>
    Calculated,
    /// <summary>Photo capture.</summary>
    Photo,
    /// <summary>Video capture.</summary>
    Video,
    /// <summary>Audio recording.</summary>
    Audio,
    /// <summary>Digital signature capture.</summary>
    Signature,
    /// <summary>Freehand sketch or annotation.</summary>
    Sketch,
    /// <summary>Barcode or QR code scan.</summary>
    Barcode,
}
