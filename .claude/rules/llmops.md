---
description: Orka AI agent / LLMOps / Evaluator kuralları — multi-dimensional scoring, provider failover, Redis telemetri
globs:
  - "Orka.Infrastructure/Services/*Agent*.cs"
  - "Orka.Infrastructure/Services/AIAgentFactory.cs"
  - "Orka.Infrastructure/Services/AgentOrchestratorService.cs"
  - "Orka.Infrastructure/Services/RedisMemoryService.cs"
  - "Orka.Core/DTOs/AgentMetrics.cs"
  - "Orka.Core/Interfaces/IRedisMemoryService.cs"
  - "scripts/llm-eval/**"
alwaysApply: false
---

# LLMOps Kuralları — Ajan, Değerlendirici, Telemetri

## Multi-Dimensional Evaluator Şeması

`EvaluatorAgent` her AI cevabını 3 boyutta puanlar (RAG-triad esintili):

| Boyut | Ölçek | Ne ölçer |
|---|---|---|
| `pedagogy` | 1-5 | Açıklama öğretici mi? Seviyeye uygun mu? |
| `factual` | 1-5 | Bilgi doğru mu? Halüsinasyon var mı? |
| `context` | 1-5 | Kullanıcı sorusuna gerçekten yanıt veriyor mu? |

- **Overall** (1-10) `(p+f+c)/15 * 10` formülü ile normalize edilir.
- `factual < 3` → `hallucinationRisk = true` otomatik tetiklenir.
- Halüsinasyon riski varsa **altın örneğe kaydedilmez** (yanlış bilgi pekişmesin).
- Sub-skorlar `[HALL] [F:x P:y C:z] text...` formatında feedback string'ine gömülür — SQL migration gerektirmez, backward-compat korunur.

## Provider Failover Chain

```
1) GitHub Models (Primary)
2) Groq (Fallback 1)
3) Gemini (Fallback 2)
```

- `AIAgentFactory` TTFT ölçümünü **ilk tokenda** yapar (stream için), non-stream için toplam süreyi ölçer.
- Her çağrı tamamlandığında `RecordMetricSafe(role, latency, success, provider)` çağrısı — **async fire-and-forget değil**, `Task.Run + try/catch + ILogger` wrapper ile güvenli.
- Redis metric key pattern: `orka:metrics:{agentRole}` (LTRIM max 100, TTL 24h).
- Model Mix widget'ında Primary ≥ 85% → sağlıklı sayılır.

## Agent Envanteri

| Agent | Sorumluluk | Nerede çağrılır |
|---|---|---|
| `TutorAgent` | Ders anlatımı, quiz üretimi, cevap değerlendirme | AgentOrchestratorService |
| `DeepPlanAgent` | Müfredat planı oluşturma | `GenerateAndSaveDeepPlanAsync` |
| `AnalyzerAgent` | Tamamlanma analizi (tüm konu bitti mi?) | `AnalyzeCompletionAsync` |
| `SummarizerAgent` | Wiki özet üretimi (idempotent) | `SummarizeAndSaveWikiAsync` |
| `WikiAgent` | Wiki belgesine dayalı Q&A (SSE stream) | WikiController |
| `KorteksAgent` | İnternet araştırması + wiki entegrasyonu (Tavily) | WikiController |
| `EvaluatorAgent` | Her ajan cevabını multi-dim skorlar | Tüm streamlerden sonra |
| `GraderAgent` | Quiz cevaplarını puanlar | Quiz akışı |

## Token & Maliyet Takibi

- `ITokenCostEstimator` (Singleton) — 2026 fiyat tablosu içerir.  `Estimate(model, input, output)` → `(tokens, costUSD)`.
- Heuristik: `CharsPerToken = 3.5` (güvenli tahmin).
- 3 noktada doldurulur: `SaveAiMessage`, `ProcessMessageAsync`, auto-progression.
- `Message.TokenCount`, `Message.EstimatedCost`, `Session.TotalTokensUsed`, `Session.TotalCostUSD` otomatik kümülatif.

## Yeni Ajan Eklerken Zorunlu Adımlar

1. `Orka.Core.Enums.AgentRole` enum'a ekle.
2. `appsettings.json > AI:Models:<Rol>` altında primary/fallback model tanımla.
3. `Program.cs`'de `AddScoped<IYeniAgent, YeniAgent>()` kayıt.
4. `AIAgentFactory.CompleteChatAsync` zaten rolü tanıyor — ek değişiklik gerekmez.
5. `EvaluatorAgent` zaten string `agentRole` aldığı için ek değişiklik gerekmez.
6. Redis metric key otomatik üretilir (`orka:metrics:<rol>`).

## Task.Run Kuralı

**Asla çıplak fire-and-forget yazma.**  Her `Task.Run` bloğu **mutlaka** try/catch + logger içerir:

```csharp
_ = Task.Run(async () =>
{
    try { await DoWorkAsync(); }
    catch (Exception ex) { _logger.LogError(ex, "[Agent] fire-and-forget hatası"); }
});
```

## LLM Değerlendirme Scriptleri

- `scripts/llm-eval/promptfooconfig.yaml` — scenario-based LLM eval config (promptfoo)
- `scripts/healthcheck.mjs` — tam sistem sağlık denetim scripti (Node.js, cross-platform)

Çalıştırma:
```bash
# Tam sağlık taraması (backend ayakta olmalı)
node scripts/healthcheck.mjs

# LLM kalitesi eval (promptfoo)
cd scripts/llm-eval && npx promptfoo eval
```
