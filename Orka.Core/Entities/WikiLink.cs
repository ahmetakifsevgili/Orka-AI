namespace Orka.Core.Entities;

public sealed class WikiLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid SourcePageId { get; set; }
    public WikiPage SourcePage { get; set; } = null!;
    public Guid? TargetPageId { get; set; }
    public WikiPage? TargetPage { get; set; }
    public string TargetPageKey { get; set; } = string.Empty;
    public string LinkType { get; set; } = "related";
    public decimal Strength { get; set; } = 1m;
    public string CreatedBy { get; set; } = "system";
    public string SafeLabel { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
