namespace Honua.Mobile.PlatformSmoke;

internal sealed record PlatformSmokeResult
{
    public required bool Success { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required string Platform { get; init; }

    public required long ElapsedMilliseconds { get; init; }

    public int? FeatureCount { get; init; }

    public string? ProviderName { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorType { get; init; }

    public static PlatformSmokeResult Passed(
        string platform,
        long elapsedMilliseconds,
        int featureCount,
        string providerName)
        => new()
        {
            Success = true,
            CompletedAt = DateTimeOffset.UtcNow,
            Platform = platform,
            ElapsedMilliseconds = elapsedMilliseconds,
            FeatureCount = featureCount,
            ProviderName = providerName,
        };

    public static PlatformSmokeResult Failed(
        string platform,
        long elapsedMilliseconds,
        string errorMessage,
        string errorType)
        => new()
        {
            Success = false,
            CompletedAt = DateTimeOffset.UtcNow,
            Platform = platform,
            ElapsedMilliseconds = elapsedMilliseconds,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
        };
}
