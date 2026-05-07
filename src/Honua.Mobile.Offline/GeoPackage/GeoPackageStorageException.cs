namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>
/// Sanitized problem details for mobile storage failures.
/// </summary>
public sealed class GeoPackageStorageProblem
{
    /// <summary>
    /// Stable problem type URI.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Short, consumer-safe problem title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Stable storage error code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Consumer-safe problem detail.
    /// </summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Exception raised when GeoPackage storage operations fail with provider-specific errors.
/// </summary>
public sealed class GeoPackageStorageException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="GeoPackageStorageException"/>.
    /// </summary>
    /// <param name="message">Redacted error message safe for SDK consumers.</param>
    public GeoPackageStorageException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="GeoPackageStorageException"/>.
    /// </summary>
    /// <param name="message">Redacted error message safe for SDK consumers.</param>
    /// <param name="innerException">Original exception retained for diagnostics.</param>
    public GeoPackageStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="GeoPackageStorageException"/>.
    /// </summary>
    /// <param name="problem">Sanitized storage problem details.</param>
    public GeoPackageStorageException(GeoPackageStorageProblem problem)
        : base(problem.Title)
    {
        Problem = problem;
    }

    /// <summary>
    /// Initializes a new <see cref="GeoPackageStorageException"/>.
    /// </summary>
    /// <param name="problem">Sanitized storage problem details.</param>
    /// <param name="innerException">Original exception retained for diagnostics.</param>
    public GeoPackageStorageException(GeoPackageStorageProblem problem, Exception innerException)
        : base(problem.Title, innerException)
    {
        Problem = problem;
    }

    /// <summary>
    /// Sanitized problem details suitable for UI or SDK error propagation.
    /// </summary>
    public GeoPackageStorageProblem? Problem { get; }
}
