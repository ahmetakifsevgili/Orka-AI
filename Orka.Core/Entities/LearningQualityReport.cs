namespace Orka.Core.Entities;

public sealed class LearningQualityReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string GraphQualityStatus { get; set; } = "unknown";
    public string AssessmentQualityStatus { get; set; } = "unknown";
    public string MasteryConfidenceStatus { get; set; } = "unknown";
    public string TutorPolicyComplianceStatus { get; set; } = "unknown";
    public string EventHealthStatus { get; set; } = "unknown";
    public string SourceGroundingStatus { get; set; } = "unknown";
    public string ToolExecutionHealthStatus { get; set; } = "unknown";
    public string ArtifactRenderHealthStatus { get; set; } = "unknown";
    public string LearnerEvidenceStatus { get; set; } = "unknown";
    public string RagQualityStatus { get; set; } = "unknown";
    public string EvidenceCoverageStatus { get; set; } = "unknown";
    public string EvidenceProviderHealthStatus { get; set; } = "unknown";
    public string EvidenceFreshnessStatus { get; set; } = "unknown";
    public string ForumSignalUsageStatus { get; set; } = "none";
    public string EvidenceCitationCoverageStatus { get; set; } = "unknown";
    public string TutorPedagogyStatus { get; set; } = "unknown";
    public string AssessmentCalibrationStatus { get; set; } = "unknown";
    public string AdaptiveReadiness { get; set; } = "unknown";
    public string ItemBankHealth { get; set; } = "unknown";
    public string TraceHealth { get; set; } = "unknown";
    public string StandardsAlignmentStatus { get; set; } = "unknown";
    public decimal CaseLikeCoverage { get; set; }
    public decimal QtiLikeCoverage { get; set; }
    public decimal CaliperXapiCoverage { get; set; }
    public decimal? TutorPedagogyScore { get; set; }
    public int CriticalPedagogyViolationCount { get; set; }
    public string ReportJson { get; set; } = "{}";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}
