# Orka AI — v1 → v3 Yol Haritası

**Tarih:** 2026-05-01
**Bağlam:** v1.0 sertleştirme (security/perf/observability) tamamlandı. Bu doküman `docs/architecture/ORKA_MASTER_GUIDE.md` §15 ve "Gelecek Vizyonu" bölümündeki 36 özelliği effort/dependency açısından sürüm sürüm sıralar.

---

## v1.0 — Stabilite (TAMAM, dondurulup release adayı)

`docs/reviews/2026-05-01-code-review.md` raporundaki K1–K7, O1, O4–O11, O16–O19, D5, D6, D8, D10, D12 maddeleri uygulandı. Build, smoke, frontend bundle temiz. Üyelik/billing v1 kapsam dışı (kullanıcı kararı).

**Bilinçli atlanmış:** D12 (SK Kernel singleton — plugin DI refactor gerekir), O2/O3 (god class refactor — EducatorCore akışı koruması altında), D1/D3/D4/D7/D11 (düşük öncelik temizlikler).

---

## v1.1 — "Mevcut altyapıyı yüzeye çıkar" (1-2 hafta)

Yeni dış API yok. Backend'de mevcut servisler/plugin'ler var; frontend ve prompt cilası ile değer çıkarılır.

| ID | Özellik | Mevcut altyapı | Effort |
|---|---|---|---|
| 1 | Analitik Dashboard UI | DashboardController + LearningSignal + SkillMastery + AgentEvaluations | M |
| 7 | Wikipedia tuning | WikipediaPlugin (SK) | S |
| 13 | Akademik tuning | AcademicSearchPlugin (SK) | S |
| 18 | Flashcard UI + backend extension | SummarizerAgent (yeni metod gerekir) | M |
| 22 | TTS UI yaygınlaştırma | EdgeTtsService + AudioOverviewService (Wiki'de var, Chat'e bağlanacak) | S |
| 31 | Bookmark | yeni tablo + endpoint + UI | M |
| 34 | A/B test admin view | scripts/llm-eval (promptfoo) | S |

**Toplam:** ~12-15 gün. Yeni 0 dış API. Stabil v1.1 release.

---

## v1.2 — "Pedagoji çekirdek" (3-4 hafta)

| ID | Özellik | Bağımlılık | Effort |
|---|---|---|---|
| 3 | Push Notification (FCM + PWA) | — (16 ve 17'yi etkinleştirir) | L |
| 5 | API maliyet izleme + kota | TokenCostEstimator var | M |
| 15 | XP / Rozet kazanma kuralları | User.TotalXP/CurrentStreak field var | M |
| 16 | Spaced Repetition Engine | Ebbinghaus + Redis schedule + #3 | L |
| 17 | Daily Challenge | SkillMastery + #3 | M |
| 30 | Hata Taksonomi + remedial trigger | RemediationPlan + LearningSignal | M |

**Toplam:** ~18-22 gün. Pedagoji ölçülebilir hale gelir.

---

## v2.0 — "Veri besleme + ses/görsel" (5-7 hafta)

| ID | Özellik | Notlar | Effort |
|---|---|---|---|
| 6 | YouTube Transcript RAG | mevcut plugin + Cohere embeddings + Redis vector index | M |
| 10 | Wolfram Alpha | matematik halüsinasyon engelleyici | M |
| 21 | WebGL Math (Desmos / FunctionPlot) | etkileşimli graf | L |
| 23 | STT (Whisper / Web Speech) | sesli soru sorma | L |
| 24 | Kod Diff (Monaco diff editor) | mevcut IDE genişletmesi | S |
| 14 | Skill Tree görsel ağaç | Topic hiyerarşisi var | L |
| 4 | Profil sayfası | XP, rozet, geçmiş | M |

**Toplam:** ~28-35 gün. Bu noktada NotebookLM rakibi seviyesi.

---

## v2.1 — "Çevre veri + niş öğrenim" (3-4 hafta)

| ID | Özellik | Effort |
|---|---|---|
| 2 | Sentiment Detection (Hume veya LLM) | L |
| 8 | NewsAPI | S |
| 9 | OpenWeatherMap | S |
| 11 | CoinGecko / Alpha Vantage | S |
| 12 | GitHub REST API kod örnek | M |
| 19 | Leaderboard (opt-in) | M |
| 25 | Çoklu dosya IDE | L |
| 26 | Regex / SQL Playground | M |
| 27 | Pronunciation Checker | M |
| 28 | Kimya Molekül (PubChem + 3Dmol) | L |

---

## v3.0 — "Platform + B2B" (sonraki çeyrek)

| ID | Özellik | Effort |
|---|---|---|
| 20 | 3D Model (R3F + Sketchfab) | XL |
| 29 | MEB / OpenSyllabus müfredat eşleştirme | L |
| 32 | Topluluk soru bankası | L |
| 33 | Teacher Dashboard (multi-tenant) | XL |
| 35 | Collaborative Learning (Yjs CRDT) | XL |
| 36 | AI Ödev Kontrol (intihal + doğruluk) | L |

---

## Bağımlılık zinciri (kritik bağlar)

- **#3 (FCM)** olmadan **#16 (Spaced Repetition) + #17 (Daily Challenge)** kullanıcıya ulaşmaz → v1.2'de paralel.
- **#15 (XP)** olmadan **#14 (Skill Tree) + #19 (Leaderboard)** anlamsız.
- **#5 (kota)** v2.0'dan önce gelmeli — yoksa harici API faturası kontrolden çıkar.
- **#6 (YouTube RAG)** Cohere Embeddings + Redis Vector index gerektirir; CohereEmbeddingService var ama vector index yok.
- **#33 (Teacher)** multi-tenant kullanıcı modeli gerektirir → şu anki tek-kullanıcı modelinde XL.
- **#35 (Yjs)** WebSocket + CRDT altyapısı sıfır → v3.0.

---

## Effort skalası

| Etiket | Süre | Kapsam |
|---|---|---|
| S | <1 gün | Tek dosya, prompt finetune, küçük UI |
| M | 1-3 gün | 2-5 dosya, yeni endpoint+UI |
| L | 3-7 gün | Yeni servis veya yeni dış API entegrasyonu |
| XL | 1+ hafta | Yeni altyapı (CRDT, multi-tenant, 3D engine) |

---

## Mevcut envanter (zaten çalışan, tekrar yazılmayacak)

`ORKA_MASTER_GUIDE.md`'de listelendiği gibi: TutorAgent, QuizAgent, DeepPlanAgent, SummarizerAgent, KorteksAgent, EvaluatorAgent, IntentClassifierAgent, SupervisorAgent, AnalyzerAgent, GraderAgent, PistonService (Judge0 RCE), LearningSignalService, SkillMasteryService, AudioOverviewService, ClassroomService, LearningSourceService, RedisMemoryService, Mermaid render, Pollinations görsel, WikiDrawer/WikiMainPanel, InteractiveIDE, SystemHealthHUD, Onboarding Tour.

V4'te eklenenler: WikipediaPlugin, AcademicSearchPlugin, YouTubeTranscriptPlugin (SK bridge), CohereEmbeddingService, EducatorCoreService, NotebookLM source pinning + trust strip.
