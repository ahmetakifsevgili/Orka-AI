using System;
using System.Collections.Generic;

namespace Orka.Core.Entities;

public class WikiPage
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public Topic Topic { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? SessionId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? PlanStepId { get; set; }
    public Guid? ParentWikiPageId { get; set; }
    public string PageKey { get; set; } = string.Empty;
    public string PageType { get; set; } = "concept";
    public string? ConceptKey { get; set; }
    public string? ParentConceptKey { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Auto-Wiki tarafından üretilen tam Markdown gövde.</summary>
    public string? Content { get; set; }
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string? SafeSummary { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public int OrderIndex { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    public ICollection<WikiBlock> Blocks { get; set; } = new List<WikiBlock>();
    public ICollection<Source> Sources { get; set; } = new List<Source>();
}
