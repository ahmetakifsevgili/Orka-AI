using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly OrkaDbContext _db;

    public ProfileController(OrkaDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("xp")]
    public async Task<IActionResult> GetXp()
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == GetUserId());
        if (user == null) return NotFound();

        var level = (user.TotalXP / 100) + 1;
        var xpInLevel = user.TotalXP % 100;
        return Ok(new
        {
            totalXP = user.TotalXP,
            currentStreak = user.CurrentStreak,
            lastActiveDate = user.LastActiveDate,
            level,
            xpInLevel,
            xpToNextLevel = 100 - xpInLevel
        });
    }

    [HttpGet("badges")]
    public async Task<IActionResult> GetBadges()
    {
        var userId = GetUserId();
        var badges = await _db.UserBadges
            .AsNoTracking()
            .Include(ub => ub.Badge)
            .Where(ub => ub.UserId == userId)
            .OrderByDescending(ub => ub.EarnedAt)
            .Select(ub => new
            {
                ub.Badge.Id,
                ub.Badge.Code,
                ub.Badge.Name,
                ub.Badge.Description,
                ub.Badge.IconKey,
                ub.Badge.RuleType,
                ub.Badge.Threshold,
                ub.EarnedAt
            })
            .ToListAsync();

        return Ok(new { badges });
    }
}
