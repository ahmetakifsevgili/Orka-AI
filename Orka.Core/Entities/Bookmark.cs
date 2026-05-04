using System;

namespace Orka.Core.Entities;

/// <summary>
/// Kullanıcının chat akışında kaydetmek istediği özel mesaj/anlık not.
/// Bir Message'a referans tutar; mesaj silinirse cascade ile düşer.
/// </summary>
public class Bookmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;

    /// <summary>Kullanıcının eklediği isteğe bağlı kısa not.</summary>
    public string? Note { get; set; }

    /// <summary>Etiket / kategori (örn: "tekrar et", "soru-bankası").</summary>
    public string? Tag { get; set; }

    public DateTime CreatedAt { get; set; }
}
