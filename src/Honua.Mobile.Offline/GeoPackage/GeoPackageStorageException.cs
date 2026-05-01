namespace Honua.Mobile.Offline.GeoPackage;

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
}
