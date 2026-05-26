using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class OrkaCodeLearningIdeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CodeLearningIde_NewLearnerDegradesSafelyToDiagnostic()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-new");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE New");

        var ide = await GetIdeAsync(user, topicId);
        var json = JsonSerializer.Serialize(ide, JsonOptions);

        Assert.Equal(topicId, ide.TopicId);
        Assert.Contains(ide.ReadinessStatus, new[] { "thin_evidence", "limited" });
        Assert.Contains(ide.RecommendedActions, a => a.ActionType == "start_code_diagnostic");
        Assert.False(ide.ActiveExercise.PreSubmitKeyVisible);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_OneErrorDoesNotOverreactToHeavyRepair()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-one-error");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE One Error");
        await SeedCodeSignalsAsync(factory, user.UserId, topicId, LearningSignalTypes.IdeCompileError, count: 1, phase: "compile");

        var ide = await GetIdeAsync(user, topicId);

        Assert.NotEqual("syntax_repair", ide.Mode);
        Assert.DoesNotContain(ide.RecommendedActions, a => a.ActionType == "repair_syntax_error" && a.Priority == "high");
        Assert.Contains(ide.RecommendedActions, a => a.ActionType is "practice_code_concept" or "ask_tutor");
        AssertSafePayload(JsonSerializer.Serialize(ide, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_RepeatedSyntaxErrorsCreateSyntaxRepair()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-syntax");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Syntax");
        await SeedCodeSignalsAsync(factory, user.UserId, topicId, LearningSignalTypes.IdeCompileError, count: 3, phase: "compile");

        var ide = await GetIdeAsync(user, topicId);

        Assert.Equal("syntax_repair", ide.Mode);
        Assert.Equal("syntax", ide.RepeatedErrorSummary.DominantErrorType);
        Assert.Contains(ide.RecommendedActions, a => a.ActionType == "repair_syntax_error");
        Assert.Contains(ide.NotebookHandoffs, h => h.HandoffType == "create_code_repair_pack");
        AssertSafePayload(JsonSerializer.Serialize(ide, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_RepeatedRuntimeErrorsCreateRuntimeRepair()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-runtime");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Runtime");
        await SeedCodeSignalsAsync(factory, user.UserId, topicId, LearningSignalTypes.IdeRuntimeError, count: 2, phase: "run");

        var ide = await GetIdeAsync(user, topicId);

        Assert.Equal("runtime_error_repair", ide.Mode);
        Assert.Contains(ide.RecommendedActions, a => a.ActionType == "repair_runtime_error");
        Assert.Contains(ide.TutorHandoffs, h => h.HandoffType == "ask_tutor");
        AssertSafePayload(JsonSerializer.Serialize(ide, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_RepeatedTestFailuresCreateTestRepair()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-test");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Test Failure");
        await SeedCodeSignalsAsync(factory, user.UserId, topicId, LearningSignalTypes.IdeTestFailure, count: 2, phase: "test");

        var ide = await GetIdeAsync(user, topicId);

        Assert.Equal("test_failure_repair", ide.Mode);
        Assert.Contains(ide.RecommendedActions, a => a.ActionType == "repair_test_failure");
        Assert.Contains("repeated_test_failure", ide.ReasonCodes);
        AssertSafePayload(JsonSerializer.Serialize(ide, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_BlankAttemptsCreateDiagnosticNotMisconceptionCertainty()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-blank");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Blank");
        await SeedCodeSignalsAsync(factory, user.UserId, topicId, LearningSignalTypes.IdeBlankAttempt, count: 2, phase: "blank");

        var ide = await GetIdeAsync(user, topicId);
        var json = JsonSerializer.Serialize(ide, JsonOptions);

        Assert.Equal("concept_practice", ide.Mode);
        Assert.Contains(ide.RecommendedActions, a => a.ActionType == "start_code_diagnostic");
        Assert.DoesNotContain("misconception", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_StableSuccessAllowsContinueWithoutGuarantee()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-success");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Success");
        await SeedCodeSignalsAsync(factory, user.UserId, topicId, LearningSignalTypes.IdeRunCompleted, count: 2, phase: "run", positive: true);

        var ide = await GetIdeAsync(user, topicId);
        var json = JsonSerializer.Serialize(ide, JsonOptions);

        Assert.Equal("continue_project", ide.Mode);
        Assert.Contains(ide.RecommendedActions, a => a.ActionType is "continue_code_project" or "take_code_checkpoint");
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_BlockedRuntimeReturnsSafeLimitedContract()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-blocked");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Blocked");

        var ide = await GetIdeAsync(user, topicId, language: "powershell");

        Assert.Equal("blocked_runtime", ide.Mode);
        Assert.Equal("blocked", ide.RuntimeReadiness.Status);
        Assert.Contains(ide.RecommendedActions, a => a.ActionType == "runtime_blocked");
        Assert.Contains(ide.RuntimeWarnings, w => w.WarningCode == "code_runtime_blocked");
        AssertSafePayload(JsonSerializer.Serialize(ide, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_RedactsRuntimePayloadMarkers()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-redact");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Redact");
        await SeedUnsafeRuntimeSignalAsync(factory, user.UserId, topicId);

        var ide = await GetIdeAsync(user, topicId);
        var json = JsonSerializer.Serialize(ide, JsonOptions);

        Assert.Contains(ide.RuntimeWarnings, w => w.WarningCode is "unsafe_payload_blocked" or "local_path_redacted");
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_DashboardAndNotebookConsumeCodeContract()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-dashboard");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Code IDE Dashboard");
        await SeedCodeSignalsAsync(factory, user.UserId, topicId, LearningSignalTypes.IdeCompileError, count: 2, phase: "compile");

        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today")
            ?? throw new InvalidOperationException("Dashboard payload missing.");
        var notebook = await user.Client.GetFromJsonAsync<OrkaNotebookStudioProDto>($"/api/notebook-studio/pro?topicId={topicId}")
            ?? throw new InvalidOperationException("Notebook Studio Pro payload missing.");

        Assert.NotNull(dashboard.CodeLearningIde);
        Assert.Equal("syntax_repair", dashboard.CodeLearningIde!.Mode);
        Assert.Contains(notebook.RecommendedPacks, p => p.PackType == "code_repair_pack");
        AssertSafePayload(JsonSerializer.Serialize(new { dashboard.CodeLearningIde, notebook }, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task CodeLearningIde_BlocksCrossUserTopicAndSessionAccess()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "code-ide-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Private Code Topic");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, owner.UserId, topicId, DateTime.UtcNow);

        var byTopic = await other.Client.GetAsync($"/api/code/learning-ide?topicId={topicId}");
        var bySession = await other.Client.GetAsync($"/api/code/learning-ide?sessionId={sessionId}");

        Assert.Equal(HttpStatusCode.NotFound, byTopic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bySession.StatusCode);
    }

    private static async Task<OrkaCodeLearningIdeDto> GetIdeAsync(
        CoordinationTestUser user,
        Guid topicId,
        string? language = "python")
    {
        var path = $"/api/code/learning-ide?topicId={topicId}";
        if (!string.IsNullOrWhiteSpace(language)) path += $"&language={Uri.EscapeDataString(language)}";
        var response = await user.Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaCodeLearningIdeDto>())!;
    }

    private static async Task SeedCodeSignalsAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        string signalType,
        int count,
        string phase,
        bool positive = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        for (var i = 0; i < count; i++)
        {
            db.LearningSignals.Add(new LearningSignal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                SignalType = signalType,
                SkillTag = "python",
                TopicPath = "python-loops",
                Score = positive ? 100 : 0,
                IsPositive = positive,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    language = "python",
                    success = positive,
                    phase,
                    safeTutorSummary = positive ? "Kod basariyla calisti." : "Kod hatasi safe category ile izlendi.",
                    durationMs = 12,
                    truncated = false
                }),
                CreatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedUnsafeRuntimeSignalAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        db.LearningSignals.Add(new LearningSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SignalType = LearningSignalTypes.IdeRuntimeError,
            SkillTag = "python",
            TopicPath = "python-files",
            Score = 0,
            IsPositive = false,
            PayloadJson = JsonSerializer.Serialize(new
            {
                language = "python",
                success = false,
                phase = "run",
                runtimeError = "Traceback (most recent call last): File C:\\secret\\app.py rawToolPayload apiKey=abc token_marker_secret_value stackTrace ownerId userId",
                safeTutorSummary = "Runtime error at C:\\secret\\app.py apiKey=abc rawToolPayload stackTrace",
                durationMs = 4,
                truncated = false
            }),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static void AssertSafePayload(string json, Guid userId)
    {
        var unsafeMarkers = new[]
        {
            "rawPrompt",
            "hiddenPrompt",
            "systemPrompt",
            "developerPrompt",
            "rawProviderPayload",
            "rawSourceChunk",
            "rawToolPayload",
            "debugTrace",
            "localPath",
            "apiKey",
            "secret",
            "token_marker_secret_value",
            "answerKey",
            "correctAnswer",
            "stackTrace",
            "ownerId",
            "userId",
            "rawTranscript"
        };

        foreach (var marker in unsafeMarkers)
        {
            Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official curriculum complete", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
    }
}
