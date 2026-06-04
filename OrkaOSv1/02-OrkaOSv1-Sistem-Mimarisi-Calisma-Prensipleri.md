# OrkaOS v1 - Sistem Mimarisi ve Calisma Prensipleri

Tarih: 2026-06-04  
Durum: Mevcut sistemin mimari kapanis dokumani  
Kapsam: Orka'nin ana modul mimarisi, veri ayrimi, provider reliability, guvenlik, learning trace ve calisma prensipleri

## 1. OrkaOS Nedir?

OrkaOS, ogrencinin ogrenme surecini yoneten cok modullu bir AI learning operating system'dir. Sistem tek bir chat panelinden ibaret degildir. Ana amac, ogrencinin hedefini anlayip bunu plan, ders, kaynak, wiki, soru bankasi, quiz, sesli tekrar ve learning trace ile kanitli bir ogrenme akisina cevirmektir.

Ana moduller:

- Tutor
- Planlama / DeepPlan / TieredPlanner
- Diagnostic quiz
- Soru bankasi
- Wiki
- OrkaLM kaynak defteri
- Notebook Studio
- Audio Study Room
- Learning memory / mastery
- Source evidence lifecycle
- AI provider reliability
- Security/privacy gate

## 2. Temel Mimari Kararlar

### 2.1 Wiki ve OrkaLM ayrimi

Wiki:

- Normal ders akisi
- Konu/kavram sayfalari
- PlanStep/Tutor/QuestionBank baglantilari
- Wiki graph, backlinks, block refs
- Kaynak upload yok

OrkaLM:

- Kaynak yukleme
- Source/source chunk/citation/source notebook
- Kaynak uzerinden briefing, quiz, flashcard, slide, diagram, audio
- Source graph, source backlinks, source chunk refs
- Wiki'ye otomatik yazmaz

Iki sistem arasinda su an otomatik sync yoktur. Bu karar, veri karismasini ve yanlis context kullanimi riskini azaltir.

### 2.2 Feature parity

Wiki'de olan her Notebook Studio ozelligi OrkaLM'de de vardir. Fark yalnizca context'tir.

```text
Wiki context
= WikiPage / Concept / PlanStep / Tutor / QuestionBank

OrkaLM context
= Source / SourceChunk / Citation / SourceNotebook
```

### 2.3 Surface contract

Ortak contract:

```json
{
  "surface": "wiki | orkalm",
  "contextType": "wiki_page | source_notebook",
  "wikiPageId": "only for wiki",
  "sourceId": "only for orkalm",
  "crossSurfaceSync": false
}
```

Bu contract audio, notebook artifact, export preview, classroom/study room ve e2e test payload'larinda korunur.

## 3. Calisma Prensipleri

### 3.1 Kanitli ogrenme

Orka, ogrencinin durumunu tahmin ederken yalnizca AI'nin soyledigine dayanmaz. Quiz attempts, learning signals, knowledge tracing, concept mastery, source evidence, tutor trace ve plan quality snapshot gibi kanitlardan yararlanir.

Prensip:

- Kanit yoksa `needs_diagnostic`.
- Gercek zayiflik kaniti varsa `needs_repair`.
- Sadece concept graph metadata'si yeni kullaniciyi repair akisina sokmaz.
- Zayif plan/ince output kabul edilmez.

### 3.2 Fake fallback yok

Strict roller:

- Quiz
- Diagnostic
- DeepPlan
- TieredPlanner

Bu roller in-memory/fake fallback uretmez. Provider fail ederse ya gercek external fallback denenir ya da hata yukari tasinir. Bu, sistemin "profesyonel" olmasi icin kritik karardir.

### 3.3 Public payload guvenligi

Public endpoint'ler raw `StateJson`, `SnapshotJson`, `RawPayloadJson`, raw source chunk ve provider payload tasimaz. Public cevaplar safe projection ve safe DTO uzerinden gelir.

### 3.4 Kaynak iddiasinda tevazu

Kaynak evidence dusukse sistem bunu "kesin kaynakli cevap" gibi sunmaz. Audio metadata ve retention notes icinde "kanit sinirli" uyarisi tasinir.

### 3.5 Aktif hatirlama

Orka'nin pedagojik merkezi pasif okuma degil aktif hatirlamadir:

- Briefing -> checkpoint
- Study guide -> quiz
- Flashcard -> review
- Yanlis cevap -> repair loop
- Audio overview -> study room question

## 4. Ana Veri Nesneleri

### 4.1 Learning entities

- Topic
- Session
- Message
- LearningSignal
- QuizRun
- QuizAttempt
- ConceptMastery
- KnowledgeTracingState
- ActiveLessonSnapshot
- StudentContextSnapshot
- PlanQualitySnapshot

### 4.2 Wiki entities

- WikiPage
- WikiBlock
- WikiGraph
- WikiLink
- WikiPagePractice
- WikiCopilot context

### 4.3 OrkaLM entities

- LearningSource
- SourceChunk
- Citation
- SourceEvidenceBundle
- SourceNotebook
- SourceQuestionThread
- SourceConceptLink
- SourceConceptGraph

### 4.4 Notebook/Artifact entities

- LearningNotebookPack
- LearningArtifact
- NotebookExportPreview
- SlideDeckOutline
- MindMap
- UMLDiagram
- AudioOverviewJob
- ClassroomSession / StudyRoomSession
- ClassroomInteraction

## 5. Modul Bazli Sistem Baglantilari

### 5.1 Tutor

Girdi:

- User message
- Active topic/session
- Wiki context
- Source context
- Learning memory
- Plan quality snapshot
- Tool capability context

Cikti:

- Safe tutor response
- Learning signals
- Artifact references
- Next action
- Quiz/practice hooks

Baglantilar:

- ChatController
- TutorAgent / AgentOrchestrator
- LearningSignalService
- TutorTurnStateAssembler
- QuizAttemptRecorder
- Wiki/Source services

### 5.2 Planlama

Girdi:

- User goal
- Approved intent
- Diagnostic profile
- Concept graph
- Knowledge tracing
- Source evidence

Cikti:

- DeepPlan modules
- Plan sequence
- Readiness state
- Diagnostic questions
- Milestones
- Repair loops

Ana kararlar:

- No evidence -> diagnostic-first
- Weak evidence -> repair-first
- Thin plan -> quality gate
- Provider thin output -> fail-fast

### 5.3 Soru Bankasi

Girdi:

- Curriculum/standards
- Concept/outcome mapping
- Question draft
- Import packages
- Quiz evidence

Cikti:

- Reviewable question
- Published question
- Rich content block
- Stimulus
- Analytics
- Quality review signal

Ana ilke:

- Answer key pre-submit sızmaz.
- Wrong answer remediation pedagojik olur.
- Soru bankasi tutor/planlama ile learning signal uzerinden baglanir.

### 5.4 Wiki

Girdi:

- Topic/concept
- Plan step
- Tutor trace
- Question bank trace
- Wiki blocks

Cikti:

- Wiki page
- Properties/tags/backlinks
- Graph view
- Study guide, quiz, flashcard
- Slide/UML/mindmap
- Audio overview

Sinir:

- Kaynak upload yok.
- OrkaLM source chunk otomatik okunmaz.

### 5.5 OrkaLM

Girdi:

- Uploaded file/source
- Source chunks
- Citation set
- Source notebook
- Source Q&A

Cikti:

- Source-grounded briefing
- Study guide
- Glossary
- Timeline
- Quiz/flashcard
- Slide/UML/mindmap
- Audio overview
- Source study room

Sinir:

- Wiki'ye otomatik yazmaz.
- Wiki page blocks otomatik okunmaz.

### 5.6 Audio Study Room

Girdi:

- AudioOverviewJob
- Surface/context
- Transcript
- Caption track
- Active segment
- User question

Cikti:

- Script/audio
- Caption
- Classroom/session answer
- Browser TTS fallback
- Learning signal

Ana contract:

```json
{
  "surface": "wiki | orkalm",
  "contextType": "wiki_page | source_notebook",
  "audioMode": "brief | deep_dive | critique | debate",
  "ttsQuality": "draft | standard | studio",
  "crossSurfaceSync": false
}
```

## 6. AI Provider Reliability Katmani

Roller:

- Summarizer
- Tutor
- Quiz
- Diagnostic
- DeepPlan
- TieredPlanner
- Evaluator
- IntentClassifier

Prensipler:

- Strict role -> fake fallback yok
- Max attempts config'e uyar
- External fallback gercek provider uzerinden olur
- Provider failure saklanmaz
- Diagnostic/DeepPlan kalite gate'i provider output'unu kontrol eder

## 7. Diagnostic Quiz Prensibi

Question count heuristic:

| Scope | Soru sayisi |
|---|---:|
| Dar/tek konu/syntax | 15 |
| Orta konu/sinav/topic practice | 20 |
| SQL index/query optimization | 24 |
| Algorithm + data structures gibi genis kapsam | 25 |
| Default | 15 |

Deterministic fallback blueprint:

- Metadata-rich
- `assessmentItemId`
- `conceptKey`
- `learningOutcomeIds`
- `scoringRule`
- Diverse question types
- Distractor rationales
- Dogru cevabi option metninde sizdirmaz

Onemli not:

Bu blueprint production'da provider yerine gizlice kullanilmaz. Contract/test guvenligi icindir.

## 8. Release Quality Gate

Tamamlanan son dogrulama:

```text
API full: 634/634
Infrastructure full: 176/176
Frontend typecheck/build/smoke: passed
Playwright full: 5 passed / 1 skipped
Notebook Studio contract: 2/2
```

Kalan release karar basliklari:

- Dirty worktree commit/PR kapsam ayrimi
- Skipped life-proof test'in akibet karari
- Edge-TTS/studio voice ortam stabilizasyonu
- Soru sayisi heuristic review
- Ileride manuel Wiki-OrkaLM bridge karari

## 9. Guvenlik ve Gizlilik

Auth:

- Refresh token JSON body'den cikti.
- Refresh token HttpOnly cookie ile doner.
- Access token/user response contract'i korunur.

Trace:

- Raw state public controller'da parse edilmez.
- Projection helper safe output uretir.

Source:

- Raw chunk public UI'da gosterilmez.
- Citation label/highlight kullanilir.
- Evidence limited durumlari metadata/warning olarak tasinir.

AI:

- Provider payload public response'a sizmaz.
- Thin/generic output release gate'e takilir.

## 10. Sistem Ruhunu Ozetleyen Kurallar

1. Kanit yoksa olcmeye basla.
2. Zayif kanit varsa onar.
3. Kaynak varsa kaynakla konus, yoksa kaynak iddiasi kurma.
4. Wiki ve OrkaLM'i karistirma.
5. AI cevabini kalite kapisindan gecir.
6. Public response guvenli projection olsun.
7. Pasif icerigi aktif hatirlama akisina cevir.
8. Sesli deneyimi transcript/caption/fallback ile erisilebilir yap.

