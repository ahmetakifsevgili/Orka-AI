namespace Orka.Core.DTOs.Topic;

public class CreateTopicRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Category { get; set; }
}

public class UpdateTopicRequest
{
    public string? Title { get; set; }
    public string? Emoji { get; set; }
    public bool? IsArchived { get; set; }
}
