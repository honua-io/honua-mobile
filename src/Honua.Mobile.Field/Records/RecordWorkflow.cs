namespace Honua.Mobile.Field.Records;

/// <summary>
/// Enforces the allowed status transitions for a <see cref="FieldRecord"/>.
/// Valid transitions: Draft -> Submitted -> Approved/Rejected, and Rejected -> Submitted.
/// </summary>
public sealed class RecordWorkflow
{
    /// <summary>
    /// Determines whether a transition from <paramref name="from"/> to <paramref name="to"/> is allowed.
    /// </summary>
    /// <param name="from">The current record status.</param>
    /// <param name="to">The desired target status.</param>
    /// <returns><see langword="true"/> if the transition is valid; otherwise <see langword="false"/>.</returns>
    public bool CanTransition(RecordStatus from, RecordStatus to)
    {
        return (from, to) switch
        {
            (RecordStatus.Draft, RecordStatus.Submitted) => true,
            (RecordStatus.Submitted, RecordStatus.Approved) => true,
            (RecordStatus.Submitted, RecordStatus.Rejected) => true,
            (RecordStatus.Rejected, RecordStatus.Submitted) => true,
            _ when from == to => true,
            _ => false,
        };
    }

    /// <summary>
    /// Transitions <paramref name="record"/> to <paramref name="targetStatus"/>, updating timestamps as appropriate.
    /// </summary>
    /// <param name="record">The record to transition.</param>
    /// <param name="targetStatus">The desired target status.</param>
    /// <param name="transitionTimeUtc">Optional explicit timestamp; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transition is not allowed.</exception>
    public void Transition(FieldRecord record, RecordStatus targetStatus, DateTimeOffset? transitionTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!CanTransition(record.Status, targetStatus))
        {
            throw new InvalidOperationException($"Invalid status transition from {record.Status} to {targetStatus}.");
        }

        record.Status = targetStatus;
        var now = transitionTimeUtc ?? DateTimeOffset.UtcNow;

        if (targetStatus == RecordStatus.Submitted)
        {
            record.SubmittedAtUtc = now;
            record.CompletedAtUtc = null;
        }

        if (targetStatus is RecordStatus.Approved or RecordStatus.Rejected)
        {
            record.CompletedAtUtc = now;
        }
    }
}
