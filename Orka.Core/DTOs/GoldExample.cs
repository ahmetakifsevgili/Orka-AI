namespace Orka.Core.DTOs;

/// <summary>
/// EvaluatorAgent tarafından 9-10 puan alan başarılı diyalog çiftleri.
/// Redis'te "orka:gold:{topicId}" listesinde tutulur (max 10, TTL 30 gün).
/// TutorAgent bu örnekleri kendi prompt'una few-shot olarak enjekte eder.
/// </summary>
public record GoldExample(
    string UserMessage,
    string AgentResponse,
    int    Score,
    string CreatedAt   // ISO-8601 string — DateTime.UtcNow.ToString("O")
);
