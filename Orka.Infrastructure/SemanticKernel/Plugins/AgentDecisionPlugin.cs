using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

public sealed class AgentDecisionPlugin
{
    [KernelFunction, Description("Create a non-mutating structured agent decision for orchestration hints.")]
    public Task<string> CommitDecision(string action, string reason, double confidence = 0.5)
    {
        var safeConfidence = Math.Clamp(confidence, 0, 1);
        var payload = new
        {
            action = string.IsNullOrWhiteSpace(action) ? "continue_tutor" : action.Trim(),
            reason = string.IsNullOrWhiteSpace(reason) ? "No reason supplied." : reason.Trim(),
            confidence = safeConfidence,
            committed = false,
            note = "Decision is advisory in accepted validation runtime; durable state changes remain controller/service-owned."
        };

        return Task.FromResult(JsonSerializer.Serialize(payload));
    }
}
