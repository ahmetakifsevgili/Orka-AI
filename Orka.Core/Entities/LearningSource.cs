using System;
using System.Collections.Generic;

namespace Orka.Core.Entities;

public class LearningSource
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string SourceType { get; set; } = "document";
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public string Status { get; set; } = "ready";
    public string? ErrorMessage { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<SourceChunk> Chunks { get; set; } = new List<SourceChunk>();
}
