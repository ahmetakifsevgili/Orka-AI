using Orka.Infrastructure.Utilities;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class LogPrivacyGuardTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string WindowsPath = @"C:\secret\file.txt";

    [Fact]
    public void SafeId_ReturnsStableReferenceWithoutRawGuid()
    {
        var first = LogPrivacyGuard.SafeId(UserId, "usr");
        var second = LogPrivacyGuard.SafeId(UserId, "usr");

        Assert.Equal(first, second);
        Assert.StartsWith("usr_", first);
        Assert.DoesNotContain(UserId.ToString(), first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UserId.ToString("N"), first, StringComparison.OrdinalIgnoreCase);
        Assert.False(LogPrivacyGuard.ContainsUnsafeMarker(first));
    }

    [Fact]
    public void SafeTextRef_HashesArbitraryTextWithoutEchoingIt()
    {
        const string learnerText = "student private note: my family phone is 555-111-2222";

        var reference = LogPrivacyGuard.SafeTextRef(learnerText, "msg");

        Assert.StartsWith("msg_", reference);
        Assert.DoesNotContain("student private note", reference, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("555-111-2222", reference, StringComparison.OrdinalIgnoreCase);
        Assert.False(LogPrivacyGuard.ContainsUnsafeMarker(reference));
    }

    [Fact]
    public void SafeMessage_RemovesUnsafeMarkersGuidsAndPaths()
    {
        var raw = $"rawPrompt systemPrompt developerPrompt rawProviderPayload rawSourceChunk rawToolPayload debugTrace apiKey secret token answerKey correctAnswer stackTrace ownerId userId {UserId} {WindowsPath}";

        var safe = LogPrivacyGuard.SafeMessage(raw, 400);

        Assert.DoesNotContain(UserId.ToString(), safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(WindowsPath, safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("systemPrompt", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developerPrompt", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawToolPayload", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debugTrace", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContainsUnsafeMarker_FlagsRawIdentifiersPathsAndSensitiveMarkers()
    {
        Assert.True(LogPrivacyGuard.ContainsUnsafeMarker(UserId.ToString()));
        Assert.True(LogPrivacyGuard.ContainsUnsafeMarker(WindowsPath));
        Assert.True(LogPrivacyGuard.ContainsUnsafeMarker("rawProviderPayload"));
        Assert.True(LogPrivacyGuard.ContainsUnsafeMarker("answerKey"));
        Assert.False(LogPrivacyGuard.ContainsUnsafeMarker(LogPrivacyGuard.SafeId(UserId, "usr")));
    }

    [Fact]
    public void SafeExceptionType_OnlyReturnsExceptionClassName()
    {
        var exception = new InvalidOperationException($"boom at {WindowsPath} rawPrompt");

        var safe = LogPrivacyGuard.SafeExceptionType(exception);

        Assert.Equal("InvalidOperationException", safe);
        Assert.DoesNotContain("boom", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(WindowsPath, safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", safe, StringComparison.OrdinalIgnoreCase);
    }
}
