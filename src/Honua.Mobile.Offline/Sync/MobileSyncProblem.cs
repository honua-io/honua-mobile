using System.Net;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Sdk;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Stable per-feature <c>applyEdits</c> error-classification codes published by Honua Server in
/// the HTTP-200 edit result envelope (honua-server <c>GeoServicesEditErrorCodes</c>, #2251). These
/// are a documented contract: a code never changes meaning once published, so clients branch on the
/// code rather than parsing the (sanitized, free-form) error description.
/// </summary>
internal static class GeoServicesEditErrorCodes
{
    /// <summary>Unclassified / fallback failure (unexpected provider error).</summary>
    public const int Generic = 1000;

    /// <summary>Update/delete object id missing, non-numeric, or unresolvable (request-shape error).</summary>
    public const int InvalidObjectId = 1001;

    /// <summary>The update target feature does not exist (or is hidden by row-level security).</summary>
    public const int NotFound = 1002;

    /// <summary>Delete conflict: the delete target was already removed, typically by another writer.</summary>
    public const int DeleteConflict = 1003;

    /// <summary>Update conflict: optimistic-concurrency/version mismatch — the row changed since read.</summary>
    public const int UpdateConflict = 1004;

    /// <summary>The feature is locked by another editor/session (HTTP 423 semantics).</summary>
    public const int Locked = 1005;

    /// <summary>Invalid attributes/geometry, attribute-rule, or contingent-value violation (request-shape error).</summary>
    public const int ValidationFailed = 1006;

    /// <summary>Denied by an owner-based edit policy.</summary>
    public const int NotPermitted = 1007;

    /// <summary>Rolled back because a sibling operation failed under <c>rollbackOnFailure=true</c>.</summary>
    public const int RolledBack = 1008;
}

/// <summary>
/// Sanitized sync failure category safe to surface in mobile sync results.
/// </summary>
public enum MobileSyncProblemCategory
{
    /// <summary>Network, gRPC, HTTP, timeout, or reachability problem.</summary>
    Transport,
    /// <summary>Server-side version conflict.</summary>
    Conflict,
    /// <summary>Local offline storage problem.</summary>
    LocalStorage,
    /// <summary>Malformed queued payload or unsupported operation.</summary>
    InvalidOperation,
    /// <summary>Unclassified failure.</summary>
    Unknown,
}

/// <summary>
/// Sanitized sync problem information used for queue state, result failures, and telemetry.
/// </summary>
/// <param name="Category">Failure category.</param>
/// <param name="Message">Safe user-facing message.</param>
/// <param name="Retryable">Whether the failed operation can be retried.</param>
public sealed record MobileSyncProblem(MobileSyncProblemCategory Category, string Message, bool Retryable);

/// <summary>
/// Exception raised when sync cannot continue, with provider-specific exception details redacted from the public message.
/// </summary>
public sealed class MobileSyncException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="MobileSyncException"/>.
    /// </summary>
    /// <param name="problem">Sanitized problem details.</param>
    /// <param name="innerException">Original exception retained for diagnostics.</param>
    public MobileSyncException(MobileSyncProblem problem, Exception innerException)
        : base(problem.Message, innerException)
    {
        Problem = problem;
    }

    /// <summary>
    /// Sanitized sync problem details.
    /// </summary>
    public MobileSyncProblem Problem { get; }
}

/// <summary>
/// Shared mapper from provider-specific exceptions to sanitized mobile sync problems.
/// </summary>
public static class MobileSyncProblemHelper
{
    private const string TransportRetryMessage = "Sync transport is unavailable; the operation will retry.";
    private const string TransportTimeoutMessage = "Sync transport timed out; the operation will retry.";
    private const string LocalStorageMessage = "Local offline sync storage is unavailable.";
    private const string UnknownRetryMessage = "Sync operation failed; the operation will retry.";

    /// <summary>
    /// Maps an exception to a sanitized sync problem.
    /// </summary>
    /// <param name="exception">Exception to classify.</param>
    /// <returns>Sanitized problem details.</returns>
    public static MobileSyncProblem FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is MobileSyncException syncException)
        {
            return syncException.Problem;
        }

        if (exception is GeoPackageStorageException || IsSqliteException(exception))
        {
            return new MobileSyncProblem(MobileSyncProblemCategory.LocalStorage, LocalStorageMessage, Retryable: true);
        }

        if (exception is HonuaMobileApiException apiException)
        {
            return FromStatusCode(apiException.StatusCode, apiException.Message);
        }

        if (exception is HttpRequestException)
        {
            return new MobileSyncProblem(MobileSyncProblemCategory.Transport, TransportRetryMessage, Retryable: true);
        }

        if (exception is TaskCanceledException)
        {
            return new MobileSyncProblem(MobileSyncProblemCategory.Transport, TransportTimeoutMessage, Retryable: true);
        }

        if (IsGrpcException(exception))
        {
            return new MobileSyncProblem(MobileSyncProblemCategory.Transport, TransportRetryMessage, Retryable: true);
        }

        return new MobileSyncProblem(MobileSyncProblemCategory.Unknown, UnknownRetryMessage, Retryable: true);
    }

    /// <summary>
    /// Maps an exception to an upload result without exposing provider-specific exception names.
    /// </summary>
    /// <param name="exception">Exception to classify.</param>
    /// <returns>Upload result matching the sanitized problem.</returns>
    public static UploadResult ToUploadResult(Exception exception)
    {
        var problem = FromException(exception);
        return ToUploadResult(problem);
    }

    /// <summary>
    /// Maps a sanitized problem to an upload result.
    /// </summary>
    /// <param name="problem">Sanitized problem.</param>
    /// <returns>Upload result matching the sanitized problem.</returns>
    public static UploadResult ToUploadResult(MobileSyncProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return new UploadResult
        {
            Outcome = problem.Category == MobileSyncProblemCategory.Conflict
                ? UploadOutcome.Conflict
                : problem.Retryable
                    ? UploadOutcome.RetryableFailure
                    : UploadOutcome.FatalFailure,
            Message = problem.Message,
        };
    }

    /// <summary>
    /// Maps a server edit error code to a sync problem.
    /// </summary>
    /// <remarks>
    /// Handles both transport-level HTTP status codes (409/412/429/5xx) and the stable
    /// per-feature <c>applyEdits</c> classification codes Honua Server emits inside an HTTP-200
    /// result envelope (<see cref="GeoServicesEditErrorCodes"/>, honua-server #2251). The
    /// conflict classes (delete-delete, update-update, not-found) are routed to
    /// <see cref="MobileSyncProblemCategory.Conflict"/> so they flow through conflict
    /// resolution instead of being dropped as fatal; transient classes (locked, rolled-back)
    /// are marked retryable.
    /// </remarks>
    /// <param name="code">Provider edit error code.</param>
    /// <param name="message">Optional provider message.</param>
    /// <returns>Sanitized problem details.</returns>
    public static MobileSyncProblem FromErrorCode(int? code, string? message)
    {
        // Per-feature applyEdits conflict classes: surface as Conflict so the offline engine
        // reconciles them rather than discarding the edit as a fatal failure.
        if (code is GeoServicesEditErrorCodes.NotFound
                 or GeoServicesEditErrorCodes.DeleteConflict
                 or GeoServicesEditErrorCodes.UpdateConflict
                 or 409 or 412)
        {
            return new MobileSyncProblem(MobileSyncProblemCategory.Conflict, message ?? "Conflict", Retryable: false);
        }

        // Per-feature classes that can succeed on a later attempt: a held lock clears, and a
        // sibling-triggered rollback under rollbackOnFailure can re-run once the sibling is fixed.
        if (code is GeoServicesEditErrorCodes.Locked or GeoServicesEditErrorCodes.RolledBack)
        {
            return new MobileSyncProblem(
                MobileSyncProblemCategory.Transport,
                message ?? UnknownRetryMessage,
                Retryable: true);
        }

        // HTTP-style transport codes only (5xx). Bounded to < 600 so it does not swallow the
        // four-digit per-feature classification codes, which are a separate code space.
        if (code is 408 or 429 or (>= 500 and < 600))
        {
            return new MobileSyncProblem(
                MobileSyncProblemCategory.Transport,
                message ?? TransportRetryMessage,
                Retryable: true);
        }

        // Remaining per-feature classes (generic, invalid-object-id, validation, not-permitted)
        // and any other non-success code are request-shape/authorization failures that will not
        // succeed on a blind retry.
        return new MobileSyncProblem(
            MobileSyncProblemCategory.InvalidOperation,
            message ?? "Fatal error",
            Retryable: false);
    }

    /// <summary>
    /// Maps an HTTP status code to a sync problem.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="message">Optional server message.</param>
    /// <returns>Sanitized problem details.</returns>
    public static MobileSyncProblem FromStatusCode(HttpStatusCode statusCode, string? message)
    {
        if (statusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            return new MobileSyncProblem(MobileSyncProblemCategory.Conflict, message ?? "Conflict", Retryable: false);
        }

        if (statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500)
        {
            return new MobileSyncProblem(
                MobileSyncProblemCategory.Transport,
                string.IsNullOrWhiteSpace(message) ? TransportRetryMessage : message,
                Retryable: true);
        }

        return new MobileSyncProblem(
            MobileSyncProblemCategory.InvalidOperation,
            string.IsNullOrWhiteSpace(message) ? "Sync request was rejected." : message,
            Retryable: false);
    }

    private static bool IsGrpcException(Exception exception)
    {
        var typeName = exception.GetType().FullName;
        return ContainsOrdinalIgnoreCase(typeName, "Grpc") ||
               ContainsOrdinalIgnoreCase(exception.Message, "RpcException") ||
               ContainsOrdinalIgnoreCase(exception.Message, "StatusCode=");
    }

    private static bool IsSqliteException(Exception exception)
    {
        var typeName = exception.GetType().FullName;
        return ContainsOrdinalIgnoreCase(typeName, "Sqlite") ||
               ContainsOrdinalIgnoreCase(exception.Message, "SQLite Error");
    }

    private static bool ContainsOrdinalIgnoreCase(string? value, string token)
        => value is not null && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
