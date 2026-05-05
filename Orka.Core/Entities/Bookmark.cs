using System;

namespace Orka.Core.Entities;

public class Bookmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public Guid? MessageId { get; set; }
    public Message? Message { get; set; }
    public Guid? LearningSourceId { get; set; }
    public LearningSource? LearningSource { get; set; }
    public Guid? WikiPageId { get; set; }
    public WikiPage? WikiPage { get; set; }
    public Guid? ReviewItemId { get; set; }
    public ReviewItem? ReviewItem { get; set; }
    public Guid? FlashcardId { get; set; }
    public Flashcard? Flashcard { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? Quote { get; set; }
    public string? TagsJson { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
