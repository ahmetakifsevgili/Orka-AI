using System;

namespace Orka.Core.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public Guid TokenFamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
