using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class CostAggregationService : ICostAggregationService
{
    private readonly OrkaDbContext _db;

    public CostAggregationService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<CostAggregationReportDto> GetReportAsync(int days = 7, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var since = DateTime.UtcNow.AddDays(-days);

        var records = await _db.CostRecords
            .AsNoTracking()
            .Where(c => c.OccurredAt >= since)
            .Select(c => new
            {
                Date = c.OccurredAt.Date,
                c.AgentRole,
                Provider = c.Provider ?? "unknown",
                c.EstimatedTokens,
                c.EstimatedCostUsd
            })
            .ToListAsync(ct);

        var dailySummary = records
            .GroupBy(r => r.Date)
            .Select(g => new DailyCostSummaryDto(
                DateOnly.FromDateTime(g.Key),
                g.Sum(r => r.EstimatedCostUsd),
                g.Sum(r => (long)r.EstimatedTokens),
                g.Count()))
            .OrderBy(d => d.Date)
            .ToList();

        var agentBreakdown = records
            .GroupBy(r => r.AgentRole)
            .Select(g => new AgentCostBreakdownDto(
                g.Key,
                g.Sum(r => r.EstimatedCostUsd),
                g.Sum(r => (long)r.EstimatedTokens),
                g.Count()))
            .OrderByDescending(a => a.TotalUsd)
            .ToList();

        var providerBreakdown = records
            .GroupBy(r => r.Provider)
            .Select(g => new ProviderCostBreakdownDto(
                g.Key,
                g.Sum(r => r.EstimatedCostUsd),
                g.Sum(r => (long)r.EstimatedTokens),
                g.Count()))
            .OrderByDescending(p => p.TotalUsd)
            .ToList();

        return new CostAggregationReportDto(
            dailySummary,
            agentBreakdown,
            providerBreakdown,
            records.Sum(r => r.EstimatedCostUsd),
            records.Sum(r => (long)r.EstimatedTokens),
            records.Count,
            days);
    }
}
