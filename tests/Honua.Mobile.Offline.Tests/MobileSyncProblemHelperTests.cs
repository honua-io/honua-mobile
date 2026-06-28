using System.Net;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

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

    [Fact]
    public void FromException_InvalidJsonApiException_IsTransportAndRetryable()
    {
        // A success response with malformed/truncated JSON now carries a 502
        // (transport garble) rather than the default StatusCode 0. It must classify
        // as a retryable Transport problem, not a non-retryable InvalidOperation /
        // meaningless Code 0.
        var ex = new HonuaMobileApiException(
            HttpStatusCode.BadGateway,
            "Honua mobile request returned invalid JSON.",
            responseBody: null,
            innerException: new InvalidOperationException("boom"));

        var problem = MobileSyncProblemHelper.FromException(ex);

        Assert.Equal(MobileSyncProblemCategory.Transport, problem.Category);
        Assert.True(problem.Retryable);
        Assert.Equal(UploadOutcome.RetryableFailure, MobileSyncProblemHelper.ToUploadResult(ex).Outcome);
    }

    [Fact]
    public void FromStatusCode_ClientError_RemainsNonRetryableInvalidOperation()
    {
        // Genuine client errors (4xx other than conflict/timeout/429) stay fatal so the
        // 502 transport treatment above is scoped to the garbled-body case only.
        var problem = MobileSyncProblemHelper.FromStatusCode(HttpStatusCode.BadRequest, "bad");

        Assert.Equal(MobileSyncProblemCategory.InvalidOperation, problem.Category);
        Assert.False(problem.Retryable);
    }
}
