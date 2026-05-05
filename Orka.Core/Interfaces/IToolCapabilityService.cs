using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IToolCapabilityService
{
    IReadOnlyList<ToolCapabilityDto> GetCapabilities(bool includeInternal = false);
    ToolCapabilityDto? GetCapability(string toolId, bool includeInternal = false);
}
