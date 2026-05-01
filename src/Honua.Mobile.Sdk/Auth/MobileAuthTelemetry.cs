using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Honua.Mobile.Sdk.Auth;

/// <summary>
/// Telemetry sources emitted by the Honua mobile auth layer.
/// </summary>
public static class MobileAuthTelemetry
{
    /// <summary>
    /// Activity source name for auth operations.
    /// </summary>
    public const string ActivitySourceName = "Honua.Mobile.Auth";

    /// <summary>
    /// Meter name for auth metrics.
    /// </summary>
    public const string MeterName = "Honua.Mobile.Auth";

    /// <summary>
    /// Activity source for token resolution and refresh operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> TokenRefreshes = Meter.CreateCounter<long>(
        "mobile_auth_token_refreshes_total",
        description: "Number of mobile auth token refresh attempts.");

    /// <summary>
    /// Records a token refresh attempt.
    /// </summary>
    /// <param name="result">Refresh outcome such as <c>success</c>, <c>skipped</c>, or <c>failure</c>.</param>
    public static void RecordTokenRefresh(string result)
        => TokenRefreshes.Add(1, new KeyValuePair<string, object?>("result", result));
}
