using System;

namespace Orka.Core.Entities;

public class Source
{
    public Guid Id { get; set; }
    public Guid WikiPageId { get; set; }
    public WikiPage WikiPage { get; set; } = null!;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? DurationMinutes { get; set; }
    public bool IsWatched { get; set; }
    public DateTime CreatedAt { get; set; }
}
