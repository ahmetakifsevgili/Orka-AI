using Microsoft.AspNetCore.Http;
using Orka.Core.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Orka.API.Services;

public class TenantService : ITenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public bool BypassTenantFilters => false;

    public TenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCurrentTenantId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return string.Empty;
        }

        // Keep tenant identity stable even when older tokens do not carry tenant claims.
        var user = httpContext.User;
        var tenantClaim = user?.FindFirst("tenant_id")?.Value
                          ?? user?.FindFirst("tenant")?.Value;

        if (!string.IsNullOrEmpty(tenantClaim))
        {
            return tenantClaim;
        }

        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                     ?? user?.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId.Trim()}";
        }

        return string.Empty;
    }
}
