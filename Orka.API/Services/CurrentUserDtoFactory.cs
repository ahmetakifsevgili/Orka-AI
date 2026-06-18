using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Orka.Core.DTOs.Auth;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Infrastructure.Data;

namespace Orka.API.Services;

public static class CurrentUserDtoFactory
{
    public static async Task<UserDto> CreateAsync(
        User user,
        OrkaDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var isOnboardingCompleted = await dbContext.DiagnosticProfiles
            .AsNoTracking()
            .AnyAsync(p => p.UserId == user.Id, cancellationToken);

        return new UserDto
        {
            Id = user.Id.ToString(),
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Plan = user.Plan.ToString(),
            IsOnboardingCompleted = isOnboardingCompleted,
            StorageUsedMB = user.StorageUsedMB,
            StorageLimitMB = user.StorageLimitMB,
            DailyMessageCount = user.DailyMessageCount,
            DailyLimit = GetDailyLimit(user.Plan, configuration),
            DailyResetAt = user.DailyMessageResetAt,
            CreatedAt = user.CreatedAt,
            IsAdmin = user.IsAdmin,
            Settings = new UserSettingsDto
            {
                Theme = user.Theme,
                Language = user.Language,
                FontSize = user.FontSize,
                QuizReminders = user.QuizReminders,
                WeeklyReport = user.WeeklyReport,
                NewContentAlerts = user.NewContentAlerts,
                SoundsEnabled = user.SoundsEnabled
            }
        };
    }

    private static int GetDailyLimit(UserPlan plan, IConfiguration configuration)
    {
        return plan == UserPlan.Pro
            ? configuration.GetValue("Limits:ProUserDailyMessages", 500)
            : configuration.GetValue("Limits:FreeUserDailyMessages", 50);
    }
}
