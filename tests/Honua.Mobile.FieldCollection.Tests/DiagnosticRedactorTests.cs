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
              "access_token": "access-token-secret",
              "refresh_token": "refresh-token-secret",
              "nested": { "Authorization": "Bearer auth-secret" },
              "safe": "visible"
            }
            """);

        Assert.Contains("visible", redacted);
        Assert.Contains("[redacted]", redacted);
        Assert.DoesNotContain("api-secret", redacted);
        Assert.DoesNotContain("header-secret", redacted);
        Assert.DoesNotContain("access-secret", redacted);
        Assert.DoesNotContain("access-token-secret", redacted);
        Assert.DoesNotContain("refresh-token-secret", redacted);
        Assert.DoesNotContain("auth-secret", redacted);
    }

    [Fact]
    public void RedactJson_RedactsAiCapturePayloadAndBiometricFields()
    {
        var redacted = DiagnosticRedactor.RedactJson(
            """
            {
              "voiceTranscript": "replace pump seal",
              "rawMediaPayload": "base64-photo",
              "localPath": "/private/mobile/photo.jpg",
              "faceEmbedding": "biometric-vector",
              "safeStatus": "queued"
            }
            """);

        Assert.Contains("queued", redacted);
        Assert.Contains("[redacted]", redacted);
        Assert.DoesNotContain("replace pump seal", redacted);
        Assert.DoesNotContain("base64-photo", redacted);
        Assert.DoesNotContain("/private/mobile/photo.jpg", redacted);
        Assert.DoesNotContain("biometric-vector", redacted);
    }

    [Fact]
    public void RedactSensitiveText_RedactsBearerAndTokenPairs()
    {
        var redacted = DiagnosticRedactor.RedactSensitiveText(
            "Authorization: Bearer bearer-secret refresh_token=refresh-secret");

        Assert.DoesNotContain("bearer-secret", redacted);
        Assert.DoesNotContain("refresh-secret", redacted);
        Assert.Contains("[redacted]", redacted);
    }

    [Fact]
    public void RedactUrl_StripsCredentialsQueryAndFragment()
    {
        var redacted = DiagnosticRedactor.RedactUrl(
            "https://user:pass@example.honua.test/path/to/layer?token=query-secret#fragment");

        Assert.Equal("https://example.honua.test/path/to/layer", redacted);
    }
}
