using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Orka.Core.Enums;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public UserController(OrkaDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        var dailyLimit = user.Plan == UserPlan.Pro
            ? _configuration.GetValue<int>("Limits:ProUserDailyMessages", 500)
            : _configuration.GetValue<int>("Limits:FreeUserDailyMessages", 50);

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            plan = user.Plan.ToString(),
            storageUsedMB = user.StorageUsedMB,
            storageLimitMB = user.StorageLimitMB,
            dailyMessageCount = user.DailyMessageCount,
            dailyLimit,
            dailyResetAt = user.DailyMessageResetAt,
            createdAt = user.CreatedAt
        });
    }
}
