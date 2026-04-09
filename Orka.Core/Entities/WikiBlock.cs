using System;
using Orka.Core.Enums;

namespace Orka.Core.Entities;

public class WikiBlock
{
    public Guid Id { get; set; }
    public Guid WikiPageId { get; set; }
    public WikiPage WikiPage { get; set; } = null!;
    public WikiBlockType BlockType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Source { get; set; }
    public int OrderIndex { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
