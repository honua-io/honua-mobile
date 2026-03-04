using System.Net;

namespace Honua.Mobile.Sdk;

public sealed class HonuaMobileApiException : Exception
{
    public HonuaMobileApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }
}
