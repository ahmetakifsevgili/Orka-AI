namespace Orka.Core.DTOs;

public sealed class AgenticTrustPolicyDto
{
    public string Contract { get; set; } = "agentic_trust_v1";
    public string Status { get; set; } = "active";
    public IReadOnlyList<string> GuardedSurfaces { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> IssueCategories { get; set; } = Array.Empty<string>();
}

public sealed class AgenticTrustIssueDto
{
    public string Category { get; set; } = "unknown";
    public string Severity { get; set; } = "warning";
    public string AffectedSurface { get; set; } = "unknown";
    public string UserSafeLabel { get; set; } = "Trust risk detected.";
    public string UserSafeRemediation { get; set; } = "Orka degraded this step safely.";
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgenticTrustCheckRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? CorrelationId { get; set; }
    public string Surface { get; set; } = "user_message";
    public string? Content { get; set; }
    public string? ToolId { get; set; }
    public string? Caller { get; set; }
    public string? Purpose { get; set; }
    public string? RiskLevel { get; set; }
    public bool ActiveQuizUnsubmitted { get; set; }
    public IReadOnlyList<ValidateSourceCitationDto> Citations { get; set; } = Array.Empty<ValidateSourceCitationDto>();
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class AgenticTrustCheckResultDto
{
    public string Surface { get; set; } = "unknown";
    public string Decision { get; set; } = "allow";
    public string Status { get; set; } = "safe";
    public bool Allowed { get; set; } = true;
    public IReadOnlyList<AgenticTrustIssueDto> Issues { get; set; } = Array.Empty<AgenticTrustIssueDto>();
    public IReadOnlyList<string> UserSafeWarnings { get; set; } = Array.Empty<string>();
    public Guid? RuntimeTraceId { get; set; }
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgenticTrustRuntimeSummaryDto
{
    public string Status { get; set; } = "safe";
    public int CheckCount { get; set; }
    public int BlockedCount { get; set; }
    public int DegradedCount { get; set; }
    public IReadOnlyDictionary<string, int> IssuesByCategory { get; set; } = new Dictionary<string, int>();
    public IReadOnlyList<AgenticTrustIssueDto> RecentIssues { get; set; } = Array.Empty<AgenticTrustIssueDto>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
