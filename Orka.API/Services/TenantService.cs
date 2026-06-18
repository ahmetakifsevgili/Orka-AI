using Microsoft.AspNetCore.Http;
using Orka.Core.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Orka.API.Services;

public class TenantService : ITenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAiRequestContextAccessor _aiRequestContext;

    public bool BypassTenantFilters =>
        _httpContextAccessor.HttpContext == null &&
        _aiRequestContext.Current.UserId == null;

    public TenantService(
        IHttpContextAccessor httpContextAccessor,
        IAiRequestContextAccessor aiRequestContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _aiRequestContext = aiRequestContext;
    }

    public string GetCurrentTenantId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return _aiRequestContext.Current.UserId is { } backgroundUserId
                ? $"user:{backgroundUserId}"
                : string.Empty;
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

        if (_aiRequestContext.Current.UserId is { } scopedUserId)
        {
            return $"user:{scopedUserId}";
        }

        return string.Empty;
    }
}
