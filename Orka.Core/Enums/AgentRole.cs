namespace Orka.Core.Enums;

/// <summary>
/// Sistemdeki ajan rolleri — AIAgentFactory model yönlendirmesinde kullanılır.
/// </summary>
public enum AgentRole
{
    Tutor,
    DeepPlan,
    /// <summary>TieredPlanner — Devasa sınav müfredatı (KPSS/YKS) için ayrı model slotu. Ağır Skeleton+Detail paralel akışları için kullanılır.</summary>
    TieredPlanner,
    Analyzer,
    Summarizer,
    Korteks,
    Supervisor,
    Grader,
    Evaluator,
    /// <summary>Peer (Akran Öğrenci) — InteractiveClassSession'da TutorAgent ile diyalog kuran ajan. appsettings.json: AI:GitHubModels:Agents:Peer:Model</summary>
    Peer
}
