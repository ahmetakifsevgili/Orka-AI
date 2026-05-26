namespace Orka.Core.DTOs;

public record DailyCostSummaryDto(DateOnly Date, decimal TotalUsd, long TotalTokens, int RequestCount);
public record AgentCostBreakdownDto(string AgentRole, decimal TotalUsd, long TotalTokens, int RequestCount);
public record ProviderCostBreakdownDto(string Provider, decimal TotalUsd, long TotalTokens, int RequestCount);
public record CostAggregationReportDto(
    IReadOnlyList<DailyCostSummaryDto> DailySummary,
    IReadOnlyList<AgentCostBreakdownDto> AgentBreakdown,
    IReadOnlyList<ProviderCostBreakdownDto> ProviderBreakdown,
    decimal TotalUsd,
    long TotalTokens,
    int TotalRequests,
    int DaysAnalyzed);
