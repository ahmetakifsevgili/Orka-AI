using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface ITextHealthService
{
    Task<TextHealthReportDto> DryRunAsync(CancellationToken ct = default);
    Task<TextHealthRepairResultDto> RepairAsync(CancellationToken ct = default);
}
