namespace Orka.Core.Interfaces;

public interface ITenantService
{
    string GetCurrentTenantId();
    bool BypassTenantFilters { get; }
}
