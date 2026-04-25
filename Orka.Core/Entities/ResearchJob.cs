using System;

namespace Orka.Core.Entities;

public enum ResearchPhase
{
    Queued,
    ManagerPlanning,
    DataFetching,
    Synthesizing,
    Editing,
    Completed,
    Failed
}

public class ResearchJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }

    public string Query { get; set; } = string.Empty;
    public ResearchPhase Phase { get; set; } = ResearchPhase.Queued;
    
    public string? FinalReport { get; set; }
    public string? Logs { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Yüklenen belgenin çıkarılmış düz metni (PDF/TXT). Swarm bu metni prompt context'ine ekler.</summary>
    public string? DocumentContext { get; set; }
    /// <summary>True ise DataFetcherAgent internete çıkar. False ise yalnızca DocumentContext analiz edilir.</summary>
    public bool RequiresWebSearch { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
