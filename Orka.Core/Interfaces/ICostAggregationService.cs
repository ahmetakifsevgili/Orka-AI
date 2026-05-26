using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface ICostAggregationService
{
    Task<CostAggregationReportDto> GetReportAsync(int days = 7, CancellationToken ct = default);
}
