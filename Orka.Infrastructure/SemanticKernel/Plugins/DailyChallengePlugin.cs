using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class DailyChallengePlugin
{
    private readonly IDailyChallengeService _dailyChallenge;

    public DailyChallengePlugin(IDailyChallengeService dailyChallenge)
    {
        _dailyChallenge = dailyChallenge;
    }

    [KernelFunction, Description("Get today's durable daily challenge for a user.")]
    public async Task<string> GetToday(Guid userId, Guid? topicId = null)
    {
        var challenge = await _dailyChallenge.GetTodayAsync(userId, topicId);
        return $"DailyChallenge {challenge.Id}: status={challenge.Status}, source={challenge.SourceType ?? "fallback"}, prompt={challenge.Prompt}";
    }
}
