namespace Honua.Mobile.Field.Records;

public sealed class RecordWorkflow
{
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
        }

        if (targetStatus is RecordStatus.Approved or RecordStatus.Rejected)
        {
            record.CompletedAtUtc = now;
        }
    }
}
