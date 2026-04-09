<div align="center">

# 🔮 Orka AI

**Kişiselleştirilmiş Öğrenme Motoru**

*Yapay zekanın öğretmenliği yeniden tanımladığı platform*

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)
[![React 19](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react)](https://react.dev)
[![EF Core](https://img.shields.io/badge/EF_Core-8.0-1A1A2E?style=flat-square)](https://learn.microsoft.com/ef)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=flat-square)](LICENSE)

</div>

---

## Nedir?

Orka AI; kullanıcının öğrenmek istediği herhangi bir konuyu **doğal dil üzerinden** alarak ona özel pedagojik bir müfredat oluşturan, dersleri anlatan, sınav yapan ve bilgi haritasını güncel tutan bir yapay zeka eğitim platformudur.

---

## Sistem Mimarisi

```
┌─────────────────────────────────────────────────────────────┐
│                        React 19 Frontend                     │
│   TopicSidebar (Tree)  │  Global Chat  │  Wiki Drawer        │
└───────────────────────────┬─────────────────────────────────┘
                            │ REST / JSON
┌───────────────────────────▼─────────────────────────────────┐
│                     .NET 8 Web API                           │
│                                                              │
│   AgentOrchestrator ──► TutorAgent                           │
│         │               DeepPlanAgent                        │
│         │               QuizAgent                            │
│         │               SummarizerAgent                      │
│         │               AnalyzerAgent                        │
│         │                                                     │
│         └──► AIServiceChain (Smart Router + Failover)        │
│                                                              │
│   EF Core 8 ──► SQL Server (LocalDB / Azure)                 │
└──────────────────────────────────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│              7 Katmanlı AI Sağlayıcı Zinciri                 │
│                                                              │
│  [0] Google Gemini 2.5 Flash  ← Primary Smart Router        │
│       ↓ (hata / 10s timeout)                                 │
│  [1] Groq  →  [2] SambaNova  →  [3] Cerebras                 │
│       →  [4] Cohere  →  [5] HuggingFace  →  [6] Mistral     │
└──────────────────────────────────────────────────────────────┘
```

---

## Temel Özellikler

### 🧠 Smart Routing
`systemPrompt` içeriğine göre görev tespiti yapar ve uygun AI modelini + konfigürasyonu seçer:

| Görev | Model | Temperature | Max Tokens |
|-------|-------|-------------|------------|
| Deep Plan / Müfredat | gemini-2.5-flash | 0.3 | 1024 |
| Ders Anlatımı | gemini-2.5-flash | 0.7 | 2048 |
| Quiz / Değerlendirme | gemini-2.5-flash | 0.2 | 512 |

### 🔗 Fault-Tolerant Failover Zinciri
Her sağlayıcı için **10 saniyelik katı timeout** uygulanır. Timeout veya hata durumunda sistem bir sonraki sağlayıcıya geçer. `IsUsableResponse()` metodu servis bazlı hata string'lerini filtreler.

### 📚 Dinamik Müfredat (DeepPlan)
AI, konunun pedagojik yapısına göre **2–10 arası** alt başlık üretir. Sabit 4 başlık kısıtı yoktur. Her alt başlık `Order` kolonu ile deterministik sıralanır.

### 🎯 Interaktif Quiz Kartları
`TutorAgent` sınav sorularını ```quiz JSON bloğu olarak üretir; frontend bunu 4 seçenekli interaktif kart olarak render eder. Kullanıcı bir seçeneğe tıklar, backend otomatik değerlendirir.

### 🗺️ Global Chat + Wiki Ayrımı
Chat ekranı hiçbir zaman kaybolmaz. Sidebar'dan konu tıklanınca **Wiki Drawer** sağ panelde açılır; chat akışına dokunulmaz.

### 🐒 Chaos Monkey
`X-Chaos-Fail: Groq` HTTP header'ı ile belirli bir sağlayıcı başarısızlığı simüle edilebilir. Zero-downtime kanıtı için entegrasyon testlerinde kullanılır.

---

## Proje Yapısı

```
Orka/
├── Orka.API/                  # ASP.NET Core 8 Web API
│   ├── Controllers/
│   ├── Middleware/
│   └── Program.cs
├── Orka.Core/                 # Domain katmanı
│   ├── Entities/
│   ├── Enums/
│   ├── Interfaces/
│   └── DTOs/
├── Orka.Infrastructure/       # Uygulama katmanı
│   ├── Data/                  # EF Core DbContext + Migrations
│   └── Services/
│       ├── AIServiceChain.cs  # Smart Router + Failover
│       ├── GeminiService.cs
│       ├── GroqService.cs
│       ├── AgentOrchestratorService.cs
│       ├── TutorAgent.cs
│       ├── DeepPlanAgent.cs
│       └── ...
└── orka-client/               # React 19 + Vite
    ├── src/
    │   ├── components/
    │   │   ├── ChatPanel.jsx
    │   │   ├── TopicSidebar.jsx
    │   │   ├── WikiDrawer.jsx
    │   │   └── ...
    │   └── pages/
    │       └── AppDashboard.jsx
    └── tests/                 # Playwright E2E testleri
```

---

## Kurulum

### Ön Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org)
- SQL Server LocalDB *(Visual Studio ile gelir)* veya SQL Server Express

### Backend

```bash
# Veritabanı oluştur
cd Orka
dotnet ef database update -p Orka.Infrastructure -s Orka.API

# API'yi başlat
cd Orka.API
dotnet run
# → http://localhost:5065
```

### Frontend

```bash
cd orka-client
npm install
npm run dev
# → http://localhost:5173
```

### Test Kullanıcısı

```
E-posta : test@orka.ai
Şifre   : TestPass123!
```

---

## E2E Testler (Playwright)

```bash
cd orka-client

# Tüm testleri çalıştır
npx playwright test

# Belirli test — tarayıcı görünür
npx playwright test continuous-flow.spec.js --headed

# Interaktif Playwright UI
npx playwright test --ui
```

| Test Dosyası | Kapsam |
|---|---|
| `continuous-flow.spec.js` | Doğal sohbet → `/plan` → Deep Plan → İlk Ders |
| `continuous-quiz-flow.spec.js` | Quiz → `[PLAYWRIGHT_PASS_QUIZ]` → Konu Geçişi |
| `chaos-failover.spec.js` | `X-Chaos-Fail: Groq` → Zero-Downtime Failover |

---

## API Yapılandırması (`appsettings.json`)

```json
"AI": {
  "Gemini":      { "ApiKey": "...", "ModelDeepPlan": "gemini-2.5-flash", ... },
  "Groq":        { "ApiKey": "...", "Model": "llama-3.3-70b-versatile" },
  "SambaNova":   { "ApiKey": "...", "Model": "Meta-Llama-3.3-70B-Instruct" },
  "Cerebras":    { "ApiKey": "...", "Model": "llama3.1-8b" },
  "Cohere":      { "ApiKey": "...", "Model": "command-a-03-2025" },
  "HuggingFace": { "ApiKey": "...", "Model": "meta-llama/Llama-3.1-8B-Instruct" },
  "Mistral":     { "ApiKey": "...", "Model": "mistral-small-latest" }
}
```

---

## 🚀 Yakında — Stratejik API Entegrasyonları

> Aşağıdaki tüm ajan ve servisler, **Modular Plug-in mimarisi** ile sisteme dahil edilecektir.  
> Her entegrasyon bağımsız bir `IAgent` implementasyonu olarak geliştirilir; mevcut `AgentOrchestrator`
> zincirine kayıt yaptırılarak aktif hale gelir. Hiçbir çekirdek servis değiştirilmez.

---

### ⚙️ Katman 1 — Pratik & Uygulama *(The Sandbox)*

| API / Araç | Ajan | Senaryo |
|---|---|---|
| **Judge0 / Piston** | `CodeExecutorAgent` | 70+ dilde chat üzerinden anlık kod çalıştırma; C# snippet derleme, sonucu bubble'a basma |
| **Carbon** | `CodeArtAgent` | Kod bloklarını şık görsele (PNG) dönüştürme; Wiki'ye kaydetme veya indirme |
| **JSONPlaceholder** | `MockDataAgent` | API eğitimlerinde anlık sahte veri üretimi; UI/UX geliştirme demoları |

---

### 🔬 Katman 2 — Bilgi Doğrulama *(Knowledge Grounding)*

| API / Araç | Ajan | Senaryo |
|---|---|---|
| **Free Dictionary API** | `TermTooltipAgent` | "Encapsulation", "Polymorphism" gibi teknik terimler üzerine hover'da resmi tanım tooltip'i |
| **Stack Exchange API** | `SOReferencAgent` | Kod hatasında Stack Overflow'un en çok oylanan çözümünü otomatik referans olarak getirme |
| **DevDocs** | `DocSnippetAgent` | C#, React, SQL resmi dokümantasyonundan anlık snippet ve kural seti |
| **Wikipedia API** | `ConceptGroundingAgent` | KPSS Genel Kültür ve yazılım tarihi (SOLID, Design Patterns kökeni) için hızlı özet |

---

### 📊 Katman 3 — Görselleştirme & Analitik *(UX Enrichment)*

| API / Araç | Ajan | Senaryo |
|---|---|---|
| **QuickChart / Image-Charts** | `ProgressChartAgent` | Haftalık öğrenme ilerlemesi ve sınav başarılarını Bar/Pie grafiğine döküp chat'e gömme |
| **Mermaid.js** | `DiagramAgent` | Veritabanı şemaları (ERD), yazılım mimarisi diyagramları ve akış şemalarını anlık render etme |

---

### 🔒 Katman 4 — Uzmanlık & Güvenlik *(Pro-Level Features)*

| API / Araç | Ajan | Senaryo |
|---|---|---|
| **CVE Search / NIST NVD** | `VulnAdvisorAgent` | Sistem mimarisi derslerinde güncel yazılım güvenlik açıklarını sorgulama ve öğretme |
| **VirusTotal API** | `StaticAnalysisAgent` | Şüpheli kod/dosya statik analizi; siber güvenlik derslerini gerçek vaka ile destekleme |
| **CanIUse** | `BrowserCompatAgent` | Frontend derslerinde üretilen CSS/JS kodunun tarayıcı uyumluluğunu canlı teyit etme |

---

### 💼 Katman 5 — Kariyer & Gündem *(The Career Launcher)*

| API / Araç | Ajan | Senaryo |
|---|---|---|
| **Adzuna / Reed.co.uk** | `JobMatchAgent` | Kullanıcının yetenek setine (Junior .NET, QA Engineer) uygun güncel iş ilanlarını filtreleme |
| **NewsAPI** | `TrendingTopicsAgent` | Türkiye ve dünya gündemini tarayarak KPSS "Güncel Bilgiler" modülü için taze içerik |

---

> **Teknik Not — Modular Plug-in Mimarisi:**  
> Her yeni ajan, `IAgent` arayüzünü implement eden bağımsız bir sınıf olarak yazılır.  
> `Program.cs`'te `AddScoped<IMyAgent, MyAgent>()` ile kayıt yaptırılır ve  
> `AgentOrchestratorService` içindeki routing tablosuna tek satır eklenerek aktif olur.  
> Bu sayede mevcut chat akışı, quiz döngüsü veya AI zinciri **hiç değiştirilmeden** yeni yetenekler kazanır.

---

## Lisans

[MIT](LICENSE) — Ahmet Akif Sevgili
