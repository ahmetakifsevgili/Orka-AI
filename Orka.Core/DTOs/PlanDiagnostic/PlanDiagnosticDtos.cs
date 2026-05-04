using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;

namespace Orka.Core.DTOs.PlanDiagnostic;

public sealed class PlanDiagnosticStateDto
{
    public Guid PlanRequestId { get; set; }
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public string UserLevel { get; set; } = "Bilinmiyor";
    public PlanDiagnosticStatus Status { get; set; }
    public string CompressedResearchContextJson { get; set; } = string.Empty;
    public string CompressedResearchPromptBlock { get; set; } = string.Empty;
    public GroundingMode GroundingMode { get; set; }
    public int SourceCount { get; set; }
    public Guid QuizRunId { get; set; }
    public int QuizQuestionCount { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public DateTime? QuizCompletedAt { get; set; }
    public Guid? GeneratedPlanRootTopicId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class StartPlanDiagnosticRequest
{
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? TopicTitle { get; set; }
    public string? UserLevel { get; set; }
}

public sealed class StartPlanDiagnosticResponse
{
    public Guid PlanRequestId { get; set; }
    public Guid QuizRunId { get; set; }
    public Guid TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public PlanDiagnosticStatus Status { get; set; }
    public string QuestionsJson { get; set; } = "[]";
    public GroundingMode GroundingMode { get; set; }
    public int SourceCount { get; set; }
    public int QuizQuestionCount { get; set; }
}

public sealed class FinalizePlanDiagnosticRequest
{
    public Guid PlanRequestId { get; set; }
}

public sealed class FinalizePlanDiagnosticResponse
{
    public Guid PlanRequestId { get; set; }
    public PlanDiagnosticStatus Status { get; set; }
    public bool PlanGenerated { get; set; }
    public string? Message { get; set; }
    public Guid? GeneratedPlanRootTopicId { get; set; }
    public List<Guid> GeneratedTopicIds { get; set; } = [];
}

public sealed class PlanDiagnosticAnswerResponse
{
    public Guid PlanRequestId { get; set; }
    public Guid QuizRunId { get; set; }
    public PlanDiagnosticStatus Status { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public int QuizQuestionCount { get; set; }
}
