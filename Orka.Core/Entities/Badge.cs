using System;
using System.Collections.Generic;

namespace Orka.Core.Entities;

public class Badge
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconKey { get; set; } = "award";
    public string RuleType { get; set; } = "manual";
    public int? Threshold { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string? MetadataJson { get; set; }

    public ICollection<UserBadge> UserBadges { get; set; } = new List<UserBadge>();
}
