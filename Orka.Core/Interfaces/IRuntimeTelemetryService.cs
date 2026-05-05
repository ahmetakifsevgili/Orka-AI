using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IRuntimeTelemetryService
{
    Task RecordToolEventAsync(ToolTelemetryEventRequest request, CancellationToken ct = default);
    Task RecordCostAsync(CostRecordRequest request, CancellationToken ct = default);
}
