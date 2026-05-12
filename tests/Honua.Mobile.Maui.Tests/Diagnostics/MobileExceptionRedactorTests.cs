using Honua.Mobile.Maui.Diagnostics;

namespace Honua.Mobile.Maui.Tests.Diagnostics;

public sealed class MobileExceptionRedactorTests
{
    [Fact]
    public void RedactText_RemovesTokensCredentialsAndPreciseCoordinates()
    {
        var text = "GET https://alice:secret@example.test/sync?api_key=key-123&access-key=access-query&layer=sites Authorization: Bearer raw-token x-api-key=header-secret access_key=access-secret password=hunter2 lat=21.30691 lon=-157.85830";

        var redacted = MobileExceptionRedactor.RedactText(text, new MobileExceptionReportingOptions());

        Assert.NotNull(redacted);
        Assert.DoesNotContain("alice:secret", redacted);
        Assert.DoesNotContain("key-123", redacted);
        Assert.DoesNotContain("access-query", redacted);
        Assert.DoesNotContain("header-secret", redacted);
        Assert.DoesNotContain("access-secret", redacted);
        Assert.DoesNotContain("raw-token", redacted);
        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("21.30691", redacted);
        Assert.DoesNotContain("-157.85830", redacted);
        Assert.Contains("layer=sites", redacted);
        Assert.Contains(MobileExceptionRedactor.RedactedValue, redacted);
    }

    [Fact]
    public void RedactProperties_DropsSensitiveLocationFormAndAttachmentValuesByDefault()
    {
        var properties = new Dictionary<string, object?>
        {
            ["apiKey"] = "test-api-key",
            ["x-api-key"] = "header-secret",
            ["access-key"] = "access-secret",
            ["latitude"] = 21.3069,
            ["formPayload"] = "{\"owner\":\"Kai\"}",
            ["attachmentBytes"] = "raw-bytes",
            ["safeUrl"] = "https://bob:password@example.test/path?token=secret&layer=hydrants",
        };

        var redacted = MobileExceptionRedactor.RedactProperties(properties, new MobileExceptionReportingOptions());

        Assert.Equal(MobileExceptionRedactor.RedactedValue, redacted["apiKey"]);
        Assert.Equal(MobileExceptionRedactor.RedactedValue, redacted["x-api-key"]);
        Assert.Equal(MobileExceptionRedactor.RedactedValue, redacted["access-key"]);
        Assert.Equal(MobileExceptionRedactor.PreciseLocationRedactedValue, redacted["latitude"]);
        Assert.Equal(MobileExceptionRedactor.FormPayloadRedactedValue, redacted["formPayload"]);
        Assert.Equal(MobileExceptionRedactor.AttachmentContentRedactedValue, redacted["attachmentBytes"]);
        Assert.DoesNotContain("bob:password", redacted["safeUrl"]);
        Assert.DoesNotContain("secret", redacted["safeUrl"]);
        Assert.Contains("layer=hydrants", redacted["safeUrl"]);
    }

    [Fact]
    public void RedactProperties_AllowsPreciseLocationAndFormPayloadOnlyWhenExplicitlyEnabled()
    {
        var options = new MobileExceptionReportingOptions
        {
            IncludePreciseLocation = true,
            IncludeFormPayloads = true,
        };
        var properties = new Dictionary<string, object?>
        {
            ["latitude"] = 21.3069,
            ["formPayload"] = "{\"owner\":\"Kai\"}",
            ["apiKey"] = "still-redacted",
        };

        var redacted = MobileExceptionRedactor.RedactProperties(properties, options);

        Assert.Equal("21.3069", redacted["latitude"]);
        Assert.Contains("Kai", redacted["formPayload"]);
        Assert.Equal(MobileExceptionRedactor.RedactedValue, redacted["apiKey"]);
    }
}
