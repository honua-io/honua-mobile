using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.Field.Capture;

/// <summary>
/// Mobile runtime adapter for SDK-owned field form, validation, workflow, and duplicate detection contracts.
/// </summary>
public sealed class MobileFieldCaptureWorkflow
{
    private readonly DuplicateDetector _duplicateDetector;

    /// <summary>
    /// Initializes a new mobile field workflow adapter with the SDK default duplicate detector.
    /// </summary>
    public MobileFieldCaptureWorkflow()
        : this(new DuplicateDetector())
    {
    }

    /// <summary>
    /// Initializes a new mobile field workflow adapter.
    /// </summary>
    /// <param name="duplicateDetector">SDK duplicate detector.</param>
    public MobileFieldCaptureWorkflow(DuplicateDetector duplicateDetector)
    {
        _duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));
    }

    /// <summary>
    /// Applies SDK calculated field expressions to the record.
    /// </summary>
    /// <param name="form">SDK field form definition.</param>
    /// <param name="record">SDK field record.</param>
    public void ApplyCalculatedFields(FormDefinition form, FieldRecord record)
        => CalculatedFieldEvaluator.ApplyCalculatedFields(form, record);

    /// <summary>
    /// Validates a record against an SDK field form definition.
    /// </summary>
    /// <param name="form">SDK field form definition.</param>
    /// <param name="record">SDK field record.</param>
    /// <returns>SDK validation result.</returns>
    public FormValidationResult Validate(FormDefinition form, FieldRecord record)
        => FormValidator.Validate(form, record);

    /// <summary>
    /// Determines whether a record can move between SDK workflow statuses.
    /// </summary>
    /// <param name="from">Current status.</param>
    /// <param name="to">Target status.</param>
    /// <returns><see langword="true"/> when the transition is valid.</returns>
    public bool CanTransition(RecordStatus from, RecordStatus to)
        => RecordWorkflow.CanTransition(from, to);

    /// <summary>
    /// Transitions an SDK field record and updates workflow timestamps.
    /// </summary>
    /// <param name="record">SDK field record.</param>
    /// <param name="targetStatus">Target status.</param>
    /// <param name="transitionTimeUtc">Optional transition timestamp.</param>
    public void Transition(FieldRecord record, RecordStatus targetStatus, DateTimeOffset? transitionTimeUtc = null)
        => RecordWorkflow.Transition(record, targetStatus, transitionTimeUtc);

    /// <summary>
    /// Finds duplicate SDK field records using the SDK duplicate detector.
    /// </summary>
    /// <param name="existing">Existing field records.</param>
    /// <param name="candidate">Candidate record.</param>
    /// <param name="options">Duplicate detection options.</param>
    /// <returns>Potential duplicate records.</returns>
    public IReadOnlyList<PotentialDuplicate> FindPotentialDuplicates(
        IEnumerable<FieldRecord> existing,
        FieldRecord candidate,
        DuplicateDetectionOptions? options = null)
        => _duplicateDetector.FindPotentialDuplicates(existing, candidate, options);
}
