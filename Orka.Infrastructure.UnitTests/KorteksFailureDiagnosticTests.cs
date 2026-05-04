using Orka.Infrastructure.SemanticKernel.Filters;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public class KorteksFailureDiagnosticTests
{
    [Fact]
    public void Redaction_RemovesApiKeysAndBearerTokens()
    {
        var input = "Authorization: Bearer abc.def.ghi api_key=sk-abcdefghijklmnopqrstuvwxyz token=ghp_abcdefghijklmnopqrstuvwxyz";

        var sanitized = KorteksFailureDiagnostic.Sanitize(input);

        Assert.DoesNotContain("abc.def.ghi", sanitized);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", sanitized);
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
    }

    [Fact]
    public void KorteksFailureDiagnostic_IncludesStatusAndStage()
    {
        var ex = new TestProviderException(
            "Service request failed. Status: 400 (Bad Request)",
            "400",
            "{ \"error\": { \"message\": \"invalid tool response\" } }");

        var diagnostic = KorteksFailureDiagnostic.Create(
            ex,
            KorteksFailureStage.ToolCallRoundtrip,
            "Gemini",
            "gemini-2.5-flash",
            "generativelanguage.googleapis.com",
            new KorteksToolCaptureFilter());

        Assert.Contains("Stage=ToolCallRoundtrip", diagnostic);
        Assert.Contains("Status=400", diagnostic);
        Assert.Contains("Provider=Gemini", diagnostic);
        Assert.Contains("Model=gemini-2.5-flash", diagnostic);
        Assert.Contains("EndpointHost=generativelanguage.googleapis.com", diagnostic);
        Assert.Contains("invalid tool response", diagnostic);
    }

    [Fact]
    public void KorteksFailureDiagnostic_DoesNotIncludeSecretValues()
    {
        var ex = new TestProviderException(
            "Bearer secret.jwt.value failed",
            "400",
            "{ \"key\": \"AIzaSyabcdefghijklmnopqrstuvwxyz123456\", \"token\": \"tvly-dev-abcdefghijklmnopqrstuvwxyz\" }",
            new InvalidOperationException("connection string=Server=localhost;Password=supersecret"));

        var diagnostic = KorteksFailureDiagnostic.Create(
            ex,
            KorteksFailureStage.ModelStreamStart,
            "Gemini",
            "gemini-2.5-flash",
            "generativelanguage.googleapis.com",
            new KorteksToolCaptureFilter());

        Assert.DoesNotContain("secret.jwt.value", diagnostic);
        Assert.DoesNotContain("AIzaSyabcdefghijklmnopqrstuvwxyz123456", diagnostic);
        Assert.DoesNotContain("tvly-dev-abcdefghijklmnopqrstuvwxyz", diagnostic);
        Assert.DoesNotContain("supersecret", diagnostic);
        Assert.Contains("[REDACTED]", diagnostic);
    }

    private sealed class TestProviderException : Exception
    {
        public TestProviderException(string message, string statusCode, string responseContent, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseContent = responseContent;
        }

        public string StatusCode { get; }
        public string ResponseContent { get; }
    }
}
