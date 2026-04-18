namespace Orka.Core.DTOs;

/// <summary>
/// Bir ajanın tek bir çağrısına ait telemetri verisi.
/// Redis'te "orka:metrics:{agentRole}" listesinde tutulur.
/// </summary>
public record AgentCallRecord(
    long   LatencyMs,
    bool   IsSuccess,
    string Provider,   // "GitHub" | "Groq" | "Gemini"
    string RecordedAt  // ISO-8601 string
);

/// <summary>
/// Bir ajan için hesaplanmış özet metrikler (Dashboard HUD'a gönderilir).
/// </summary>
public record AgentMetricSummary(
    string AgentRole,
    double AvgLatencyMs,
    int    TotalCalls,
    int    ErrorCount,
    double ErrorRatePct,
    string LastProvider,
    string LastCallAt
);

/// <summary>
/// EvaluatorAgent'ın bir session üzerinde bıraktığı ham puan log kaydı.
/// </summary>
public record EvaluatorLogEntry(
    int    Score,
    string Feedback,
    string RecordedAt
);

/// <summary>
/// Bir LLM provider'ının kullanım dağılımı (HUD "Model Mix" widget'ı için).
/// </summary>
public record ProviderUsageStat(
    string Provider,
    int    CallCount,
    int    ErrorCount,
    double Percentage,      // Toplam çağrıların % kaçı bu provider
    double AvgLatencyMs
);
