using System;
using System.Collections.Generic;
using Orka.Core.Enums;

namespace Orka.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserPlan Plan { get; set; } = UserPlan.Free;
    public double StorageUsedMB { get; set; }
    public double StorageLimitMB { get; set; }
    public int DailyMessageCount { get; set; }
    public DateTime DailyMessageResetAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
