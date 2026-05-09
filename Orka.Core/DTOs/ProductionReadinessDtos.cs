namespace Orka.Core.DTOs;

public sealed class ProductionReadinessDto
{
    public string Status { get; set; } = "unknown";
    public IReadOnlyList<ProductionReadinessSectionDto> Sections { get; set; } = Array.Empty<ProductionReadinessSectionDto>();
    public ProviderGovernanceSummaryDto ProviderGovernance { get; set; } = new();
    public AudioRetentionSummaryDto AudioRetention { get; set; } = new();
    public RedisStreamMaintenanceSummaryDto RedisStreams { get; set; } = new();
    public DbIndexAuditSummaryDto DbIndexAudit { get; set; } = new();
    public V1RegressionGateDto RegressionGate { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProductionReadinessSectionDto
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string UserSafeLabel { get; set; } = string.Empty;
    public string UserSafeDetail { get; set; } = string.Empty;
}

public sealed class ProviderGovernanceSummaryDto
{
    public string Status { get; set; } = "unknown";
    public int ProviderCount { get; set; }
    public int HealthyProviderCount { get; set; }
    public int RecentFailureCount { get; set; }
    public decimal EstimatedCostUsdToday { get; set; }
    public IReadOnlyList<ProviderGovernanceItemDto> Providers { get; set; } = Array.Empty<ProviderGovernanceItemDto>();
}

public sealed class ProviderGovernanceItemDto
{
    public string Provider { get; set; } = "unknown";
    public string Status { get; set; } = "unknown";
    public int Calls24h { get; set; }
    public int Failures24h { get; set; }
    public long AverageLatencyMs { get; set; }
    public decimal EstimatedCostUsdToday { get; set; }
    public string UserSafeMessage { get; set; } = string.Empty;
}

public sealed class AudioRetentionSummaryDto
{
    public string Status { get; set; } = "unknown";
    public int ReadyAudioCount { get; set; }
    public int ExpiredAudioCount { get; set; }
    public int PurgedAudioCount { get; set; }
    public long StoredAudioBytes { get; set; }
    public int RetentionDays { get; set; }
}

public sealed class RedisStreamMaintenanceSummaryDto
{
    public string Status { get; set; } = "unknown";
    public int StreamCount { get; set; }
    public long MaxLength { get; set; }
    public long ApproximateTotalLength { get; set; }
    public int TrimmedStreamCount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class DbIndexAuditSummaryDto
{
    public string Status { get; set; } = "unknown";
    public int RequiredIndexCount { get; set; }
    public int MissingIndexCount { get; set; }
    public IReadOnlyList<string> MissingIndexes { get; set; } = Array.Empty<string>();
}

public sealed class V1RegressionGateDto
{
    public string Status { get; set; } = "unknown";
    public IReadOnlyList<V1RegressionScenarioDto> Scenarios { get; set; } = Array.Empty<V1RegressionScenarioDto>();
}

public sealed class V1RegressionScenarioDto
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string UserSafeLabel { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
}
