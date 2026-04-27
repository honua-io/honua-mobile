using System.Net;

namespace Honua.Mobile.Sdk;

/// <summary>
/// Exception thrown when the Honua API returns a non-success HTTP status code.
/// </summary>
public sealed class HonuaMobileApiException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="HonuaMobileApiException"/> with a message.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public HonuaMobileApiException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileApiException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that caused the API failure.</param>
    public HonuaMobileApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileApiException"/> with status code, message, and optional response body.
    /// </summary>
    /// <param name="statusCode">The HTTP status code returned by the server.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="responseBody">The raw response body, if available.</param>
    public HonuaMobileApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// The HTTP status code returned by the server.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The raw response body returned by the server, or <see langword="null"/> if unavailable.
    /// </summary>
    public string? ResponseBody { get; }
}
