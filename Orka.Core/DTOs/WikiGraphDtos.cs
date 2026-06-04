using Orka.Core.Entities;
using Orka.Core.Enums;

namespace Orka.Core.DTOs;

public sealed class WikiGraphDto
{
    public Guid TopicId { get; set; }
    public Guid? FocusPageId { get; set; }
    public string GraphStatus { get; set; } = "ready";
    public IReadOnlyList<WikiGraphPageDto> Pages { get; set; } = Array.Empty<WikiGraphPageDto>();
    public IReadOnlyList<WikiGraphLinkDto> Links { get; set; } = Array.Empty<WikiGraphLinkDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WikiGraphPageDto
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public Guid? ParentWikiPageId { get; set; }
    public Guid? PlanStepId { get; set; }
    public string PageKey { get; set; } = string.Empty;
    public string PageType { get; set; } = "concept";
    public string? ConceptKey { get; set; }
    public string? ParentConceptKey { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string? SafeSummary { get; set; }
    public string ContentReadiness { get; set; } = "skeleton";
    public bool HasLearningContent { get; set; }
    public int VisibleBlockCount { get; set; }
    public bool RequiredBlockTypesPresent { get; set; }
    public int OrderIndex { get; set; }
    public int BlockCount { get; set; }
    public WikiCurationSummaryDto Curation { get; set; } = new();
    public WikiLearningSystemBindingDto LearningSystemBinding { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WikiLearningSystemBindingDto
{
    public string Readiness { get; set; } = "unbound";
    public Guid? PlanStepId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ParentConceptKey { get; set; }
    public bool HasConceptBinding { get; set; }
    public bool HasPlanBinding { get; set; }
    public bool HasDiagnosticBinding { get; set; }
    public bool HasTutorBinding { get; set; }
    public bool HasAssessmentOrQuestionBankBinding { get; set; }
    public bool HasSourceEvidenceBinding { get; set; }
    public int DiagnosticSignalCount { get; set; }
    public int TutorSignalCount { get; set; }
    public int AssessmentSignalCount { get; set; }
    public int SourceEvidenceSignalCount { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class WikiPageQuestionSetDto
{
    public Guid PageId { get; set; }
    public Guid TopicId { get; set; }
    public string? ConceptKey { get; set; }
    public Guid PracticeSetId { get; set; }
    public string Status { get; set; } = "ready";
    public string EmptyState { get; set; } = string.Empty;
    public string Mode { get; set; } = "wiki_page_questions";
    public int TotalQuestions { get; set; }
    public IReadOnlyList<QuestionPracticeQuestionDto> Questions { get; set; } = Array.Empty<QuestionPracticeQuestionDto>();
}

public sealed class WikiPagePracticeStartRequestDto
{
    public Guid? SessionId { get; set; }
    public string? QuestionBankSource { get; set; }
    public string Mode { get; set; } = "wiki_page_practice";
    public int Count { get; set; } = 8;
}

public static class WikiLearningSystemBindingFactory
{
    public static WikiLearningSystemBindingDto From(WikiPage page, IReadOnlyCollection<WikiBlock> visibleBlocks)
    {
        var reasonCodes = new List<string>();
        var hasConceptBinding = !string.IsNullOrWhiteSpace(page.ConceptKey) ||
                                visibleBlocks.Any(block => !string.IsNullOrWhiteSpace(block.ConceptKey));
        var hasPlanBinding = page.PlanStepId is not null;
        var diagnosticSignalCount = visibleBlocks.Count(block => block.QuizAttemptId is not null ||
            block.BlockType is WikiBlockType.QuizResult or WikiBlockType.QuizReview);
        var tutorSignalCount = visibleBlocks.Count(block => block.TutorTurnStateId is not null ||
            block.BlockType is WikiBlockType.TutorExplanation);
        var assessmentSignalCount = visibleBlocks.Count(block => block.QuizAttemptId is not null ||
            block.BlockType is WikiBlockType.QuizResult or WikiBlockType.QuizReview or WikiBlockType.Checkpoint or WikiBlockType.FlashcardSeed);
        var sourceEvidenceSignalCount = visibleBlocks.Count(block => block.SourceEvidenceBundleId is not null ||
            block.BlockType is WikiBlockType.SourceExcerptSummary);

        if (hasConceptBinding) reasonCodes.Add("concept_key_present");
        if (hasPlanBinding) reasonCodes.Add("plan_step_id_present");
        if (diagnosticSignalCount > 0) reasonCodes.Add("quiz_attempt_or_review_signal_present");
        if (tutorSignalCount > 0) reasonCodes.Add("tutor_trace_signal_present");
        if (assessmentSignalCount > 0) reasonCodes.Add("assessment_or_retrieval_signal_present");
        if (sourceEvidenceSignalCount > 0) reasonCodes.Add("source_evidence_signal_present");
        if (visibleBlocks.Any(block => block.BlockType is WikiBlockType.RepairNote or WikiBlockType.MisconceptionNote))
        {
            reasonCodes.Add("repair_signal_present");
        }

        var readiness = ResolveReadiness(
            hasConceptBinding,
            hasPlanBinding,
            diagnosticSignalCount,
            tutorSignalCount,
            assessmentSignalCount,
            sourceEvidenceSignalCount);

        return new WikiLearningSystemBindingDto
        {
            Readiness = readiness,
            PlanStepId = page.PlanStepId,
            ConceptKey = page.ConceptKey,
            ParentConceptKey = page.ParentConceptKey,
            HasConceptBinding = hasConceptBinding,
            HasPlanBinding = hasPlanBinding,
            HasDiagnosticBinding = diagnosticSignalCount > 0,
            HasTutorBinding = tutorSignalCount > 0,
            HasAssessmentOrQuestionBankBinding = assessmentSignalCount > 0,
            HasSourceEvidenceBinding = sourceEvidenceSignalCount > 0,
            DiagnosticSignalCount = diagnosticSignalCount,
            TutorSignalCount = tutorSignalCount,
            AssessmentSignalCount = assessmentSignalCount,
            SourceEvidenceSignalCount = sourceEvidenceSignalCount,
            ReasonCodes = reasonCodes
        };
    }

    private static string ResolveReadiness(
        bool hasConceptBinding,
        bool hasPlanBinding,
        int diagnosticSignalCount,
        int tutorSignalCount,
        int assessmentSignalCount,
        int sourceEvidenceSignalCount)
    {
        if (!hasConceptBinding)
        {
            return "unbound";
        }

        if (hasPlanBinding && (diagnosticSignalCount > 0 || tutorSignalCount > 0 || assessmentSignalCount > 0))
        {
            return "bound";
        }

        if (hasPlanBinding || diagnosticSignalCount > 0 || tutorSignalCount > 0 || assessmentSignalCount > 0 || sourceEvidenceSignalCount > 0)
        {
            return "partially_bound";
        }

        return "concept_bound";
    }
}

public sealed class WikiCurationSummaryDto
{
    public Guid? PageId { get; set; }
    public string? PageKey { get; set; }
    public string? ConceptKey { get; set; }
    public string CurationStatus { get; set; } = "clean";
    public int RetainedSignalCount { get; set; }
    public int MergedSignalCount { get; set; }
    public int SuppressedSignalCount { get; set; }
    public int StaleSignalCount { get; set; }
    public IReadOnlyList<string> RetainedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MergedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SuppressedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> StaleSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string StudentVisibleSummary { get; set; } = "Wiki sayfasi temiz ve okunabilir durumda.";
    public string NextAction { get; set; } = "continue_learning";
}

public sealed class WikiCopilotContextDto
{
    public Guid? PageId { get; set; }
    public string? PageKey { get; set; }
    public string? ConceptKey { get; set; }
    public string PageTitle { get; set; } = string.Empty;
    public string PageType { get; set; } = "concept";
    public string CurationStatus { get; set; } = "clean";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string MasteryStatus { get; set; } = "unknown";
    public IReadOnlyList<string> WeakConcepts { get; set; } = Array.Empty<string>();
    public string RepairState { get; set; } = "none";
    public int ArtifactCount { get; set; }
    public string NotebookPackStatus { get; set; } = "not_requested";
    public WikiCopilotActionDto? PrimaryAction { get; set; }
    public IReadOnlyList<WikiCopilotActionDto> SuggestedActions { get; set; } = Array.Empty<WikiCopilotActionDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string StudentVisibleSummary { get; set; } = "Bu sayfa icin guvenli Wiki yardimi hazir.";
    public string NextAction { get; set; } = "continue_learning";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WikiCopilotActionDto
{
    public string ActionType { get; set; } = "no_action";
    public string UserSafeLabel { get; set; } = "Devam et";
    public string UserSafeDescription { get; set; } = string.Empty;
    public string TargetSurface { get; set; } = "wiki";
    public string Availability { get; set; } = "available";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SafetyWarnings { get; set; } = Array.Empty<string>();
}

public sealed class WikiGraphLinkDto
{
    public Guid Id { get; set; }
    public Guid SourcePageId { get; set; }
    public Guid? TargetPageId { get; set; }
    public string TargetPageKey { get; set; } = string.Empty;
    public string LinkType { get; set; } = "related";
    public decimal Strength { get; set; } = 1m;
    public string CreatedBy { get; set; } = "system";
    public string SafeLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateWikiLinkRequestDto
{
    public Guid SourcePageId { get; set; }
    public Guid? TargetPageId { get; set; }
    public string? TargetPageKey { get; set; }
    public string LinkType { get; set; } = "related";
    public decimal Strength { get; set; } = 1m;
    public string CreatedBy { get; set; } = "manual";
    public string? SafeLabel { get; set; }
}

public sealed class WikiGraphSyncRequestDto
{
    public Guid? ConceptGraphSnapshotId { get; set; }
    public bool IncludeTopicTreeFallback { get; set; } = true;
    public bool CreateSummaryBlocks { get; set; } = true;
}

public sealed class WikiGraphSyncResultDto
{
    public Guid TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string SyncStatus { get; set; } = "ready";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public int CreatedPageCount { get; set; }
    public int UpdatedPageCount { get; set; }
    public int CreatedLinkCount { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public WikiGraphDto Graph { get; set; } = new();
}

public sealed class CreateWikiBlockRequestDto
{
    public string BlockType { get; set; } = "manual_note";
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SourceBasis { get; set; } = "model_assisted";
    public string? Source { get; set; }
    public string? ConceptKey { get; set; }
    public string? MisconceptionKey { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public Guid? LearningArtifactId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public string Visibility { get; set; } = "normal";
}

public sealed class WikiBlockDto
{
    public Guid Id { get; set; }
    public Guid WikiPageId { get; set; }
    public string BlockType { get; set; } = "manual_note";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string SourceBasis { get; set; } = "model_assisted";
    public string? ConceptKey { get; set; }
    public string? MisconceptionKey { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public Guid? SourceEvidenceBundleId { get; set; }
    public Guid? LearningArtifactId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public string Visibility { get; set; } = "normal";
    public IReadOnlyList<string> SafetyWarnings { get; set; } = Array.Empty<string>();
    public int OrderIndex { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
