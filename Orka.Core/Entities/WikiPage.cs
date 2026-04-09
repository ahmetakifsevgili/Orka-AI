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
    public string Title { get; set; } = string.Empty;
    /// <summary>Auto-Wiki tarafından üretilen tam Markdown gövde.</summary>
    public string? Content { get; set; }
    public int OrderIndex { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<WikiBlock> Blocks { get; set; } = new List<WikiBlock>();
    public ICollection<Source> Sources { get; set; } = new List<Source>();
}
