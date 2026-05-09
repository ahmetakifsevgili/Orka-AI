namespace Orka.Core.Entities;

public sealed class StandardsExportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ExportType { get; set; } = "combined";
    public string Status { get; set; } = "ready";
    public int ItemCount { get; set; }
    public decimal CaseCoverage { get; set; }
    public decimal QtiCoverage { get; set; }
    public decimal CaliperXapiCoverage { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StandardsExportItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StandardsExportRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string StandardFamily { get; set; } = "case";
    public string EntityType { get; set; } = "learning_outcome";
    public string EntityKey { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public StandardsExportRun StandardsExportRun { get; set; } = null!;
}

public sealed class StandardsValidationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string Status { get; set; } = "unknown";
    public decimal CaseCoverage { get; set; }
    public decimal QtiCoverage { get; set; }
    public decimal CaliperXapiCoverage { get; set; }
    public int CheckedItemCount { get; set; }
    public int IssueCount { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StandardsValidationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StandardsValidationRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string StandardFamily { get; set; } = "case";
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string EntityKey { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string IssueCode { get; set; } = string.Empty;
    public string UserSafeMessage { get; set; } = string.Empty;
    public string DetailJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public StandardsValidationRun StandardsValidationRun { get; set; } = null!;
}
