using Orka.Infrastructure.Utilities;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class AiDebugLoggerTests
{
    private static readonly string[] UnsafeFragments =
    [
        "rawPrompt",
        "systemPrompt",
        "developerPrompt",
        "rawProviderPayload",
        "rawSourceChunk",
        "rawToolPayload",
        "debugTrace",
        "apiKey",
        "secret",
        "token",
        "answerKey",
        "correctAnswer",
        "stackTrace",
        "ownerId",
        "userId",
        "C:\\",
        "D:\\",
        "/Users/",
        "/home/",
        "/var/"
    ];

    [Fact]
    public void SafePreview_DoesNotExposeRawRequestContent()
    {
        var rawRequest = """
            URL: https://generativelanguage.googleapis.com/v1beta/models/gemini:generateContent?key=apiKey-secret-token
            Model: rawPrompt-model
            {"systemPrompt":"hidden","messages":[{"content":"rawSourceChunk C:\secret\file.txt answerKey correctAnswer ownerId userId"}]}
            """;

        var preview = AiDebugLogger.BuildSafeLogPreview("OPENAI", "REQUEST", rawRequest);

        Assert.Contains("provider=OPENAI", preview);
        Assert.Contains("operation=REQUEST", preview);
        Assert.Contains("contentLength=", preview);
        Assert.Contains("contentHash=", preview);
        Assert.DoesNotContain("key=", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gemini:generateContent?key", preview, StringComparison.OrdinalIgnoreCase);
        AssertNoUnsafeFragments(preview);
    }

    [Fact]
    public void SafePreview_DoesNotExposeRawResponseContent()
    {
        var rawResponse = """
            Status: 500
            {"rawProviderPayload":"provider body","debugTrace":"stackTrace here","localPath":"D:\Orka\secret.txt","token":"abc"}
            """;

        var preview = AiDebugLogger.BuildSafeLogPreview("GROQ", "RESPONSE", rawResponse);

        Assert.Contains("provider=GROQ", preview);
        Assert.Contains("operation=RESPONSE", preview);
        Assert.Contains("httpStatus=500", preview);
        Assert.Contains("redactedFieldCount=", preview);
        AssertNoUnsafeFragments(preview);
    }

    [Fact]
    public void SafePreview_DoesNotExposeArbitraryLearnerOrSourceText()
    {
        var rawResponse = """
            Status: 503
            {"error":"student private note: my family phone is 555-111-2222 source private paragraph: confidential lesson material"}
            """;

        var preview = AiDebugLogger.BuildSafeLogPreview("OPENROUTER", "ERROR", rawResponse);

        Assert.Contains("provider=OPENROUTER", preview);
        Assert.Contains("operation=ERROR", preview);
        Assert.Contains("contentLength=", preview);
        Assert.Contains("contentHash=", preview);
        Assert.DoesNotContain("student private note", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("my family phone", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source private paragraph", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confidential lesson material", preview, StringComparison.OrdinalIgnoreCase);
        AssertNoUnsafeFragments(preview);
    }

    [Fact]
    public void SafePreview_DoesNotExposeStackTraceOrExceptionMessage()
    {
        var rawError = """
            System.InvalidOperationException: boom
               at C:\secret\file.cs:line 42
            stackTrace rawToolPayload systemPrompt
            """;

        var preview = AiDebugLogger.BuildSafeLogPreview("MISTRAL", "ERROR", rawError);

        Assert.Contains("provider=MISTRAL", preview);
        Assert.Contains("operation=ERROR", preview);
        Assert.DoesNotContain("InvalidOperationException", preview, StringComparison.OrdinalIgnoreCase);
        AssertNoUnsafeFragments(preview);
    }

    [Fact]
    public void FileLogging_IsDisabledByDefaultOutsideDevelopment()
    {
        try
        {
            AiDebugLogger.DebugLoggingOverride = "false";
            AiDebugLogger.EnvironmentNameOverride = "Production";

            Assert.False(AiDebugLogger.IsFileLoggingEnabledForCurrentEnvironment());

            AiDebugLogger.DebugLoggingOverride = "true";
            Assert.False(AiDebugLogger.IsFileLoggingEnabledForCurrentEnvironment());
        }
        finally
        {
            AiDebugLogger.DebugLoggingOverride = null;
            AiDebugLogger.EnvironmentNameOverride = null;
        }
    }

    private static void AssertNoUnsafeFragments(string value)
    {
        foreach (var fragment in UnsafeFragments)
        {
            Assert.DoesNotContain(fragment, value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
