using System.Net;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Sdk;

namespace Honua.Mobile.Offline.Sync;

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
    /// <param name="code">Provider edit error code.</param>
    /// <param name="message">Optional provider message.</param>
    /// <returns>Sanitized problem details.</returns>
    public static MobileSyncProblem FromErrorCode(int? code, string? message)
    {
        if (code is 409 or 412)
        {
            return new MobileSyncProblem(MobileSyncProblemCategory.Conflict, message ?? "Conflict", Retryable: false);
        }

        if (code is 408 or 429 || (code.HasValue && code.Value >= 500))
        {
            return new MobileSyncProblem(
                MobileSyncProblemCategory.Transport,
                message ?? TransportRetryMessage,
                Retryable: true);
        }

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
