using Honua.Mobile.FieldCollection.Services.Diagnostics;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class DiagnosticRedactorTests
{
    [Fact]
    public void RedactJson_RedactsCommonSecretKeySpellings()
    {
        var redacted = DiagnosticRedactor.RedactJson(
            """
            {
              "api_key": "api-secret",
              "x-api-key": "header-secret",
              "access-key": "access-secret",
              "nested": { "Authorization": "Bearer auth-secret" },
              "safe": "visible"
            }
            """);

        Assert.Contains("visible", redacted);
        Assert.Contains("[redacted]", redacted);
        Assert.DoesNotContain("api-secret", redacted);
        Assert.DoesNotContain("header-secret", redacted);
        Assert.DoesNotContain("access-secret", redacted);
        Assert.DoesNotContain("auth-secret", redacted);
    }

    [Fact]
    public void RedactUrl_StripsCredentialsQueryAndFragment()
    {
        var redacted = DiagnosticRedactor.RedactUrl(
            "https://user:pass@example.honua.test/path/to/layer?token=query-secret#fragment");

        Assert.Equal("https://example.honua.test/path/to/layer", redacted);
    }
}
