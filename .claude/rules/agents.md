---
description: Orka AI Zeka, Swarm Mimarisi, LLMOps ve Semantic Kernel kuralları
globs:
  - "Orka.Infrastructure/Services/*Agent*.cs"
  - "Orka.Infrastructure/Services/AIAgentFactory.cs"
  - "Orka.Infrastructure/SemanticKernel/**/*.cs"
  - "scripts/llm-eval/**"
alwaysApply: false
---

# Zeka ve Swarm Kuralları (agents.md)

> [!IMPORTANT]
> **Ajanların Kişiliği ve Mimari:** Ajanların davranışı, liyakat tabanlı rotalama (N-to-N Routing) prensipleri ve RAG entegrasyonları için **DAİMA** `docs/architecture/ORKA_MASTER_GUIDE.md` dosyasına bakacaksın.

## 1. 12-Ajanlı Swarm Mimarisi
- `SupervisorAgent` ana orkestra şefidir, doğrudan kod yazmaz, diğer ajanlara iş dağıtır.
- `DeepPlanAgent` müfredat, `TutorAgent` ders/soru, `KorteksAgent` web/youtube araması, `WikiAgent` belge özetlemesi yapar.
- Yeni bir ajan eklediğinde:
  1. `Orka.Core.Enums.AgentRole` enumuna ekle.
  2. `appsettings.json > AI:Models:<Rol>` altına primary/fallback modelini yaz.
  3. `Program.cs` dosyasına `AddScoped` ile DI kaydını yap.

## 2. LLMOps ve Failover (Hata Toleransı)
- **Model Zinciri:** Tüm istekler `AIAgentFactory` üzerinden yapılır. Sıralama: `GitHub Models (Primary) -> Groq (Fallback 1) -> Gemini (Fallback 2)`.
- Hiçbir ajan doğrudan SDK (OpenAI/Groq client) çağırmaz, her zaman Factory metotlarını kullanır.
- Token maliyetleri `ITokenCostEstimator` ile hesaplanıp kümülatif kaydedilir.

## 3. Puanlama (EvaluatorAgent Triad)
- `EvaluatorAgent` her cevabı asenkron olarak 3 kritere göre (1-5 arası) değerlendirir:
  1. `Pedagoji` (Açıklama öğretici mi?)
  2. `Factual` (Doğruluk, Halüsinasyon riski var mı?)
  3. `Context` (Niyete uygun mu?)
- `Factual < 3` gelirse, ajan "Hatalar Defteri"ne işlenir ve ilerleyen prompt'larda uyarılır.

## 4. Semantic Kernel Kuralları
- Arama yetenekleri (Tavily, YouTube Transcript, Academic Search vb.) sadece `Semantic Kernel Plugins` olarak yazılmalıdır. Spagetti HTTP request'ler yasaktır.
