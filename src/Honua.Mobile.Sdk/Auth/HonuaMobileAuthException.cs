using System.Net;

namespace Honua.Mobile.Sdk.Auth;

/// <summary>
/// Exception raised when mobile authentication cannot resolve, refresh, or persist tokens.
/// </summary>
public sealed class HonuaMobileAuthException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="HonuaMobileAuthException"/>.
    /// </summary>
    /// <param name="message">Redacted error message safe for SDK consumers.</param>
    public HonuaMobileAuthException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileAuthException"/> for a failed server auth interaction.
    /// </summary>
    /// <param name="message">Redacted error message safe for SDK consumers.</param>
    /// <param name="statusCode">HTTP status code returned by the auth endpoint.</param>
    public HonuaMobileAuthException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileAuthException"/>.
    /// </summary>
    /// <param name="message">Redacted error message safe for SDK consumers.</param>
    /// <param name="innerException">Original exception retained for diagnostics.</param>
    public HonuaMobileAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// HTTP status code returned by the auth endpoint, when the failure came from a server response.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }
}
