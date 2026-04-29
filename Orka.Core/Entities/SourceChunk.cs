using System;

namespace Orka.Core.Entities;

public class SourceChunk
{
    public Guid Id { get; set; }
    public Guid LearningSourceId { get; set; }
    public LearningSource LearningSource { get; set; } = null!;
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? EmbeddingJson { get; set; }
    public string? HighlightHint { get; set; }
    public DateTime CreatedAt { get; set; }
}
