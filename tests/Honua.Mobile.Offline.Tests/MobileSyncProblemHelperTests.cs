using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.Offline.Tests;

public sealed class MobileSyncProblemHelperTests
{
    // The stable per-feature applyEdits classification codes published by Honua Server (#2251).
    [Theory]
    [InlineData(1002)] // not found (update target gone)
    [InlineData(1003)] // delete-delete conflict
    [InlineData(1004)] // update-update (optimistic concurrency) conflict
    [InlineData(409)]  // transport-level conflict
    [InlineData(412)]  // precondition failed
    public void FromErrorCode_ConflictClasses_AreClassifiedAsConflict(int code)
    {
        var problem = MobileSyncProblemHelper.FromErrorCode(code, message: null);

        Assert.Equal(MobileSyncProblemCategory.Conflict, problem.Category);
        Assert.False(problem.Retryable);

        // A conflict must drive the conflict-resolution path, not a fatal/retry outcome.
        var upload = MobileSyncProblemHelper.ToUploadResult(problem);
        Assert.Equal(UploadOutcome.Conflict, upload.Outcome);
    }

    [Theory]
    [InlineData(1005)] // locked
    [InlineData(1008)] // rolled back because a sibling failed under rollbackOnFailure
    public void FromErrorCode_TransientClasses_AreRetryable(int code)
    {
        var problem = MobileSyncProblemHelper.FromErrorCode(code, message: null);

        Assert.True(problem.Retryable);
        Assert.Equal(UploadOutcome.RetryableFailure, MobileSyncProblemHelper.ToUploadResult(problem).Outcome);
    }

    [Theory]
    [InlineData(1000)] // generic / unexpected
    [InlineData(1001)] // invalid object id
    [InlineData(1006)] // validation failed
    [InlineData(1007)] // not permitted
    public void FromErrorCode_RequestShapeClasses_AreFatal(int code)
    {
        var problem = MobileSyncProblemHelper.FromErrorCode(code, message: null);

        Assert.False(problem.Retryable);
        Assert.Equal(MobileSyncProblemCategory.InvalidOperation, problem.Category);
        Assert.Equal(UploadOutcome.FatalFailure, MobileSyncProblemHelper.ToUploadResult(problem).Outcome);
    }
}
