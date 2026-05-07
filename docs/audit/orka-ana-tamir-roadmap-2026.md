# Orka Ana Tamir Roadmap 2026

Tarih: 2026-05-07  
Durum: Ana referans tamir yol haritasi  
Kapsam: Ek ozellik kapali. Oncelik sistemi yerine oturtmak, olcmek, kopukluklari kapatmak.

## 1. Kaynaklar ve Okuma Durumu

### Repo Ici Ana Kaynak

- `docs/audit/orka-derin-arastirma-raporu-2026-05-07.md`
- `docs/audit/yasam-raporu.md`
- `docs/audit/orka-v2.10-heavy-learning-flow-eval.md`
- `docs/audit/tutor-response-scoring-report.md`
- `docs/audit/frontend-browser-qa-report.md`

### Dis/Sektor Kaynaklari

Gemini share linki: https://gemini.google.com/share/6a8818476a34

Not: Bu link mevcut otomatik arastirma ortaminda guvenilir sekilde metin olarak acilamadi. Bu nedenle icerigini dogrudan alinti veya kesin kanit olarak kullanmadim. Kullanici linkteki metni export edip repo icine eklerse bu roadmap tekrar guncellenmelidir.

Bu eksigi kapatmak icin guncel sektor/standart arastirmasi su kaynaklarla tamamlandi:

- UNESCO GenAI egitim rehberi: human-centered, privacy, ethical validation, pedagogical design.  
  https://www.unesco.org/en/articles/guidance-generative-ai-education-and-research
- 1EdTech LTI 1.3: egitim araci entegrasyon standardi, security framework ve interoperability.  
  https://site.imsglobal.org/standards/lti/lti-1p3/1p3
- cmi5/xAPI overview: learning activity tracking, score/status/time ve genis deneyim kaydi.  
  https://xapi.com/cmi5/overview/
- OpenTelemetry GenAI semantic conventions: model/tool/agent span ve event gozlemlenebilirligi.  
  https://opentelemetry.io/docs/specs/semconv/gen-ai/
- OWASP LLM/Application ve Agentic risk aileleri: prompt injection, excessive agency, tool misuse, memory/context poisoning.  
  https://docs.aws.amazon.com/prescriptive-guidance/latest/agentic-ai-security/owasp-top-ten.html  
  https://owasp.org/www-project-mcp-top-10/
- 2026 ITS + multimodal knowledge graph + RAG calismasi: PDF/video/audio kaynaklardan otomatik bilgi grafigi ve hybrid retrieval.  
  https://www.frontiersin.org/journals/computer-science/articles/10.3389/fcomp.2026.1777749/full
- 2026 dynamic/adaptive knowledge tracing arastirmalari: statik konu tablosu yerine dinamik ogrenci durumu.  
  https://www.mdpi.com/1424-8220/26/6/1878  
  https://www.sciencedirect.com/science/article/pii/S0950705126007975

## 2. Ana Hukum

Orka'nin sorunu "hicbir sey yok" degil. Repo icinde cok sayida organ var:

- StudyIntentAnalyzer
- Korteks
- PlanResearchCompressor
- PlanIntelligenceBriefBuilder
- PlanDiagnosticService
- DiagnosticQuizQualityGate
- DeepPlanAgent
- TutorAgent
- Wiki/OrkaLM
- Sources/RAG
- IDE/Piston
- LearningSignal, SkillMastery, Review, Flashcard, DailyChallenge, Bookmark
- Redis + SQL hafiza
- ToolCapabilityService
- Telemetry/Cost

Asil sorun:

1. Bu organlar Tutor merkezli tek bir pedagojik sozlesmeye yeterince baglanmiyor.
2. Live cikti kalitesi deterministic testlerden daha oynak.
3. Frontend bazi anlarda sistemi akilli ogretmen gibi degil, daginik chatbot gibi hissettiriyor.
4. Veri/hafiza katmani var; fakat `ActiveLessonSnapshot` veya net bir `StudentContextSnapshot` sozlesmesi eksik.
5. RAG/Wiki/OrkaLM kaynaklari var; fakat her ders icin otomatik, guvenilir, kaynakli bilgi grafigi yasam dongusu henuz ana omurga degil.

## 3. Tablolama Karari

Kullanici kaygisi dogru: milyonlarca konuyu tek tek tablo satiri olarak sentezlemek yanlis mimari olur.

Dogru mimari:

- Statik konu ansiklopedisi yazilmaz.
- Korteks + kaynaklar + synthesis her konu icin dinamik kavram haritasi cikarir.
- SQL/Redis sadece kalici omurgayi tutar:
  - concept node
  - prerequisite edge
  - misconception edge
  - practice edge
  - source evidence edge
  - quiz attempt
  - mistake signal
  - mastery state
  - review pressure
  - active lesson snapshot

Yani tablo "konu icerigi deposu" degil, "ogrenme izi ve kavram iliskisi hafizasi" olmalidir.

## 4. P0 Tamir Fazlari

### P0.1 - Intent Gate ve Korteks Giris Sozlesmesi

Problem:
- Kullanicinin ham mesaji direkt research/plan tarafina kacarsa sistem konuyu yanlis anlayabilir.
- Yazim hatalari, karma istekler ve alt konu ayrimi kaliteyi dogrudan etkiler.

Hedef:
- Her plan akisi once niyet analizi kartina duser.
- Onay olmadan Korteks calismaz.
- Duzeltme yapilinca niyet bastan analiz edilir.
- Korteks'e ham mesaj degil, onayli research intent + sistemsel learning-research prompt gider.

Dosyalar:
- `Orka.Infrastructure/Services/StudyIntentAnalyzer.cs`
- `Orka.Infrastructure/Services/PlanDiagnosticService.cs`
- `Orka.API/Controllers/QuizController.cs`
- `Orka-Front/src/components/ChatPanel.tsx`

Olcum:
- Java, SQL, KPSS, IELTS, matematik, yazim hatali istekler.
- Network log: onay oncesi Korteks yok.
- Heavy eval: raw prompt leak critical fail.

Kabul:
- 40+ senaryoda intent accuracy >= 85.
- Onay kapisi 100/100.
- Duzeltme akisi browser QA ile kanitli.

### P0.2 - Korteks -> Sentez -> Quiz Kalite Hatti

Problem:
- Korteks research eder; quiz/plan ham research uzerinden olusursa kalite dusuyor.
- Generic veya yanlis domain quizleri kullanici guvenini yikiyor.

Hedef:
- Korteks sadece source-aware research uretsin.
- Synthesis katmani bunu quiz/plan verisine cevirsin:
  - prerequisites
  - sub-concepts
  - common mistakes
  - practice order
  - quiz scope
  - recommended question count
  - source/video references
- Quiz asla urun etiketini cevap olarak kullanmasin.

Dosyalar:
- `PlanResearchCompressor`
- `PlanIntelligenceBriefBuilder`
- `PlanDiagnosticService`
- `DiagnosticQuizQualityGate`

Olcum:
- Quiz 15-25 soru.
- Domain disi teknoloji sizintisi yok.
- "Dogru/Yanlis yaklasim" gibi cevap sizarak soru olmaz.
- "Orka IDE/sandbox" quiz seceneginde/aciklamasinda yok.

Kabul:
- Heavy eval 40+ scenario PASS.
- Live browser QA en az 3 konu akisi.
- Quiz pedagogy score >= 80.

### P0.3 - Plan ve Tutor Pedagojik Sahiplik

Problem:
- Tutor sadece cevap veren ajan gibi kalirsa Orka'nin tum sistemi daginik araclar toplamidir.

Hedef:
- Tutor, aktif plan ve ogrenci profilini okuyan pedagojik sahip olur.
- Bilinen konular hizli tekrar/pratik.
- Zayif konular mantiksal, detayli, ornekli remediation.
- Kaynak varsa source-grounded; yoksa "kaynak yok" der.
- Coding konusunda Orka IDE/pratik aksiyonunu Tutor onerir; quiz degil.

Dosyalar:
- `TutorAgent`
- `AdaptiveLearningContextBuilder`
- `LearningSignalService`
- `SkillMastery`/`ReviewItem` servisleri

Olcum:
- Tutor response scoring:
  - plan kullaniyor mu?
  - known/weak ayrimi yapiyor mu?
  - kaynak yoksa overclaim yok mu?
  - next small step veriyor mu?

Kabul:
- Tutor deterministic rubric >= 85.
- 3 live akista Tutor plan/profile etkisi gorunur.

### P0.4 - Frontend Learning Workspace Tamiri

Problem:
- UI chatbot gibi hissederse backend zekasi gorunmez.
- Quiz, plan, Tutor, Wiki/OrkaLM, IDE farkli yuzeyler gibi dagilir.

Hedef:
- Plan status: niyet -> onay -> research -> synthesis -> quiz -> plan.
- Quiz tek kartta akar; chat'e sistem komutu sizmaz.
- OrkaLM sol navda notebook/source hafizasi olarak net durur.
- Wiki aktif kalir; OrkaLM kaynakli ders defteri gibi konumlanir.
- Audio varsa gercek/safe fallback; 0:00 sahte player yok.

Dosyalar:
- `ChatPanel`
- `QuizCard`
- `LearningPanel`
- `WikiMainPanel`
- `OrkaLM`/source notebook yuzeyi
- `ClassroomAudioPlayer`

Olcum:
- Browser QA screenshots.
- Console 500/404/Mermaid hard error yok.
- Cevap chat leakage yok.

Kabul:
- 3 live flow browser QA PASS_WITH_NOTE veya PASS.

## 5. P1 Modernizasyon Fazlari

### P1.1 - ActiveLessonSnapshot / StudentContextSnapshot

Neden:
- Tutor su anda birden fazla kaynaktan context topluyor olabilir.
- Modern ITS sistemleri dinamik learner state ister; daginik context hata uretir.

Tasarim:
- `ActiveLessonSnapshot`
  - topicId/sessionId
  - approvedIntent
  - researchBriefId
  - quizRunId
  - knownConcepts
  - weakConcepts
  - misconceptions
  - practiceQueue
  - sourceEvidenceIds
  - lastIdeSignal
  - reviewPressure
  - generatedAt / version
- SQL kalici snapshot.
- Redis current active snapshot cache.

Kabul:
- Tutor tek snapshot sozlesmesiyle context alir.
- Snapshot eskiyse yeniden kurulur, fake state uretilmez.

### P1.2 - OrkaLM/Wiki Knowledge Graph

Neden:
- 2026 ITS/RAG mimarileri PDF/video/audio kaynaklardan otomatik knowledge graph + hybrid retrieval yonune gidiyor.
- Orka'da Wiki, Sources, Audio var; fakat bunlar tam otomatik ders hafizasina donusmeli.

Tasarim:
- Source upload -> chunks -> concept extraction -> concept graph.
- Wiki page -> human-readable notebook.
- OrkaLM -> source-grounded learning workspace.
- Tutor -> graph + vector + source evidence ile cevap verir.

Kabul:
- PDF/TXT yukle -> concept map -> citation -> Tutor source answer.
- Silinen kaynak cevaplardan duser.
- YouTube pedagogy factual proof gibi gosterilmez.

### P1.3 - Knowledge Tracing ve Mastery Graph

Neden:
- Dogru/yanlis sayisi tek basina seviye degildir.
- Knowledge tracing dinamik ogrenci durumunu izler.

Tasarim:
- Her quiz item:
  - conceptId
  - cognitiveType
  - difficulty
  - misconceptionTag
- Attempt sonucu:
  - mastery delta
  - confidence
  - review pressure
  - remediation type

Kabul:
- Quiz sonucu known/weak/practice/misconception ayirir.
- Plan ve Tutor bu sinyale gore degisir.

## 6. P2 Operasyon ve Standart Fazlari

### P2.1 - OpenTelemetry GenAI

Hedef:
- Her model/tool/provider cagrisi span olarak izlenir.
- Prompt version, provider, latency, token, fallback reason, tool id, source ids baglanir.

Kabul:
- Tek session trace ile intent -> Korteks -> synthesis -> quiz -> plan -> Tutor gorunur.

### P2.2 - Agentic Security

Hedef:
- OWASP LLM/Agentic risklerine gore test:
  - prompt injection
  - tool misuse
  - excessive agency
  - memory poisoning
  - source poisoning
  - context oversharing

Kabul:
- Tool risk matrix.
- Prompt injection fixture seti.
- Source/Wiki untrusted-content guard.

### P2.3 - Learning Interop Hazirligi

Hedef:
- Orka su an B2B degil; fakat sistem standarda uygun event tasarimina sahip olsun.
- xAPI/cmi5 benzeri event semantigi ileride raporlama/portability icin referans olabilir.
- LTI 1.3 simdilik roadmap; aktif hedef degil.

Kabul:
- Internal `LearningSignal` -> xAPI-like event mapping dokumani.
- B2C odak korunur.

## 7. Acil Bug Listesi ve Sahiplik

| Oncelik | Sorun | Kanit | Cozum |
| --- | --- | --- | --- |
| P0 | Live quiz pedagojisi oynak | Kullanici ekran goruntuleri + V2.10 | Quality gate + heavy eval + live browser QA |
| P0 | Tutor sahipligi belirsiz | Derin rapor 12.1 | Tutor pedagogical contract + snapshot |
| P0 | UI chatbot hissi | Kullanici geri bildirimi | Learning workspace tamiri |
| P0 | Plan live kalite garantisi eksik | V2.10 notes | Plan output scorer + live eval |
| P1 | Wiki/OrkaLM ders hafizasi tam otomatik degil | Derin rapor 7/12 | Knowledge graph lifecycle |
| P1 | Observability modern degil | Derin rapor 9 | OTel GenAI |
| P1 | Security prompt/tool risk seti eksik | Dis standartlar | OWASP fixture suite |
| P2 | Full multilingual/mobile/payment/cloud | Kullanici karari | Simdilik kapali |

## 8. Versiyon Siralamasi

### V2.10.x - Tamir ve Olcum

- Heavy eval genislet.
- Browser QA 3 akisi zorunlu yap.
- Quiz/live plan skorlarini rapora otomatik yaz.
- Tutor response scorer'i runtime'a bagla.

### V2.11 - Student Snapshot ve Tutor Contract

- ActiveLessonSnapshot.
- Tutor prompt/context tek sozlesmeye iner.
- Plan/quiz/review/IDE sinyalleri snapshot'a baglanir.

### V2.12 - OrkaLM/Wiki Knowledge Lifecycle

- Source -> concept extraction.
- Concept graph.
- Wiki notebook.
- Tutor source-grounded answer.
- Citation deletion safety.

### V2.13 - Mastery Graph ve Review Automation

- Concept-level mastery.
- Misconception taxonomy.
- Automatic flashcard/review proposal.
- Daily challenge weak-concept targeting.

### V2.14 - Observability/Security Gate

- OpenTelemetry GenAI.
- OWASP LLM/Agentic fixture suite.
- Cost/rate-limit dashboard.
- Tool governance matrix.

### V3 - Yeni Ozellikler Acilabilir

Ancak su kosullardan sonra:
- V2.10.x-V2.14 tamir kapilari gecmis olacak.
- UX live browser QA kabul edilecek.
- Tutor merkezli dongu kanitlanacak.
- OrkaLM kaynak hafizasi Tutor tarafindan gercek kullanilacak.

V3 adaylari:
- KPSS/YKS engine.
- Audio lesson deep integration.
- 3D/focus room.
- Personal rhythm coach.
- Subscription/payment.
- Mobile.

## 9. Nihai Karar

Ek ozellik fazi kapali kalmali.

Orka once su hale gelmeli:

1. Kullanici ne calismak istedigini yazar.
2. Orka niyeti dogru anlar ve onaylatir.
3. Korteks onayli niyetle arastirir.
4. Synthesis arastirmayi egitim verisine cevirir.
5. Quiz gercek seviye olcer.
6. Plan quiz + research + user state ile cikar.
7. Tutor bu planin pedagojik sahibi olur.
8. Wiki/OrkaLM kaynak hafizasi olur.
9. IDE/quiz/review/flashcard/daily challenge ayni ogrenme izine yazar.
10. Her kritik adim test ve telemetry ile kanitlanir.

Bu saglanmadan yeni parlak ozellik eklemek sistemi toparlamaz; daha cok dagitir.
