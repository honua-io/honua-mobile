using Honua.Mobile.Field.Records;

namespace Honua.Mobile.Field.Tests;

public sealed class RecordWorkflowTests
{
    [Fact]
    public void Transition_AllowsValidStatusFlow_AndTracksDuration()
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var submittedAt = createdAt.AddMinutes(10);
        var completedAt = createdAt.AddMinutes(25);

        var record = new FieldRecord
        {
            RecordId = "record-1",
            FormId = "inspection",
            CreatedAtUtc = createdAt,
        };

        var workflow = new RecordWorkflow();
        workflow.Transition(record, RecordStatus.Submitted, submittedAt);
        workflow.Transition(record, RecordStatus.Approved, completedAt);

        Assert.Equal(RecordStatus.Approved, record.Status);
        Assert.Equal(submittedAt, record.SubmittedAtUtc);
        Assert.Equal(completedAt, record.CompletedAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(25), record.Duration);
    }

    [Fact]
    public void DuplicateDetector_FindsNearbyMatchingRecords()
    {
        var existing = new List<FieldRecord>
        {
            new()
            {
                RecordId = "r-1",
                FormId = "inspection",
                Location = new GeoPoint(21.3069, -157.8583),
                Values = { ["asset_id"] = "A-100" },
            },
            new()
            {
                RecordId = "r-2",
                FormId = "inspection",
                Location = new GeoPoint(21.3100, -157.8600),
                Values = { ["asset_id"] = "A-200" },
            },
        };

        var candidate = new FieldRecord
        {
            RecordId = "r-new",
            FormId = "inspection",
            Location = new GeoPoint(21.30691, -157.85831),
            Values = { ["asset_id"] = "A-100" },
        };

        var detector = new DuplicateDetector();
        var duplicates = detector.FindPotentialDuplicates(existing, candidate, new DuplicateDetectionOptions
        {
            MaxDistanceMeters = 50,
            MatchFieldIds = ["asset_id"],
        });

        Assert.Single(duplicates);
        Assert.Equal("r-1", duplicates[0].RecordId);
    }
}
