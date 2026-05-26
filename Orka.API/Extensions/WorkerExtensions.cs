using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Orka.Infrastructure.Services;
using Orka.API.Services;
using Orka.API.Middleware;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Utilities;

namespace Orka.API.Extensions
{
    public static class WorkerExtensions
    {
        public static IServiceCollection AddWorkers(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<BackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredService<BackgroundTaskQueue>());
            services.AddHostedService(sp => sp.GetRequiredService<BackgroundTaskQueue>());
            
            if (configuration["Testing:DisableBackgroundWorkers"] != "true")
            {
                services.AddHostedService<SrsReminderWorker>();
                services.AddHostedService<DailyChallengeWorker>();
                services.AddHostedService<RetentionCleanupWorker>();
                services.AddHostedService<RedisStreamMaintenanceWorker>();
            }

            return services;
        }
    }
}
