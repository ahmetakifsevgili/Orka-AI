# OrkaOS v1 - Ozellik Katalogu, Model ve Sistem Baglantilari

Tarih: 2026-06-04  
Durum: Feature parity ve sistem baglantilari dokumani  
Kapsam: Wiki/OrkaLM ortak ozellikleri, her ozelligin context'i, model rolu, API/artifact baglantisi, kabul kriteri

## 1. Ortak Ozellik Katalogu

Aşağıdaki ozellikler hem Wiki'de hem OrkaLM'de vardir. Fark context'tir.

| Ozellik | Wiki context | OrkaLM context | Model/rol | Cikti | Kabul kriteri |
|---|---|---|---|---|---|
| Briefing / hizli ozet | WikiPage, Concept | Source, SourceChunk | Summarizer | `briefing_doc` | Context dogru surface'ten gelir |
| Study guide | WikiPage, Tutor trace | SourceNotebook, Citation | Summarizer/Tutor | `study_guide` | Study path ve checkpoint tasir |
| Glossary | WikiBlock, Concept | SourceChunk | Summarizer | `glossary` | Terim/tanim guvenli ve kisa |
| Timeline | WikiPage | SourceNotebook | Summarizer | `timeline` | Tarih/akış varsa uretir, yoksa bos durum |
| Quiz | QuestionBank, Wiki practice | Source practice | Quiz/Diagnostic | `review_quiz` | Answer key pre-submit sizmaz |
| Flashcard | Wiki learning trace | Source notebook | Tutor/Summarizer | `flashcard_set` | Active recall formatinda |
| Slide outline | Wiki lesson flow | Source notebook | Summarizer | `slide_deck_outline` | Speaker notes + checkpoint |
| Slide preview | Wiki artifact | Source artifact | Renderer | Preview/export | Context ve sync metadata gorunur |
| Speaker notes | Wiki lesson | Source lesson | Summarizer | Notes | Ogretim dili net |
| Checkpoint questions | PlanStep/QuestionBank | Source practice | Quiz/Tutor | Questions | Kisa olcum |
| Mind map | Wiki graph | Source graph | Summarizer/Diagram | `mind_map` | Mermaid/graph data |
| UML/Mermaid | Concept flow | Source concept flow | Diagram generator | `uml_diagram` | class/sequence/state/ER/flow destek |
| Properties | Page metadata | Source metadata | System | Panel | surface/context locked |
| Tags | Wiki tags | Source tags | System | Tag list | Ayrik namespace |
| Backlinks | Wiki backlinks | Source backlinks | System | Link map | Cross-surface edge yok |
| Linked mentions | Concept mentions | Source mentions | System | Mentions | Scoped mentions |
| Block/reference | WikiBlock ref | SourceChunk ref | System | Ref list | Raw payload sizmaz |
| Graph view | Wiki graph | Source graph | Renderer | Graph | Ayrik graph |
| Templates | Wiki templates | Source templates | System | Template set | Context-aware |
| Search/filter | Wiki search | Source search | Search | Results | Ayrik sonuc |
| Export preview | Wiki export | Source export | Exporter | Markdown/manifest | Context karismaz |
| Audio overview | Wiki audio | Source audio | Summarizer + TTS | Audio/transcript/caption | Surface isolated |
| Study room ask | Wiki study room | Source study room | Classroom/Tutor | Answer script/audio fallback | Payload dogru id tasir |

## 2. Feature Context Contract

### 2.1 Wiki feature request

```json
{
  "surface": "wiki",
  "contextType": "wiki_page",
  "topicId": "...",
  "wikiPageId": "...",
  "sourceId": null,
  "crossSurfaceSync": false
}
```

### 2.2 OrkaLM feature request

```json
{
  "surface": "orkalm",
  "contextType": "source_notebook",
  "topicId": "...",
  "wikiPageId": null,
  "sourceId": "...",
  "crossSurfaceSync": false
}
```

## 3. Model Rolleri

| Rol | Kullanildigi yer | Strict mi? | Fallback davranisi |
|---|---|---:|---|
| Tutor | Chat, repair, study room | Kismi | Guvenli pedagojik fallback mumkun |
| Summarizer | Briefing, study guide, audio script | Hayir | Safe script/text fallback mumkun |
| Quiz | Quiz generation | Evet | Fake/in-memory fallback yok |
| Diagnostic | Diagnostic quiz | Evet | Provider fail-fast |
| DeepPlan | Course plan/modules | Evet | Diagnostic path'te thin fallback kaydedilmez |
| TieredPlanner | Seviye/plan | Evet | External provider fallback ile sinirli |
| Evaluator | Response/quality scoring | Kismi | Degerlendirme fail olursa ana akisi bozmayabilir |
| IntentClassifier | Plan mode/intent | Kismi | Fail-safe intent sinirli |

## 4. Ozellik Detaylari

### 4.1 Briefing

Amac:

- Konuyu/kaynagi hizli ve guvenli ozetlemek.

Wiki baglantisi:

- WikiPage
- WikiBlock
- ConceptKey
- Tutor trace

OrkaLM baglantisi:

- LearningSource
- SourceChunk
- Citation
- SourceNotebook

Model:

- Summarizer

Artifact:

- `briefing_doc`

Kabul:

- Wiki modunda source upload/citation iddiasina yaslanmaz.
- OrkaLM modunda source/citation status tasir.

### 4.2 Study Guide

Amac:

- Ogrenme yolunu, kavramlari, mini pratikleri ve checkpoint'leri tek dokumanda toplamak.

Model:

- Summarizer + Tutor

Baglantilar:

- PlanQualitySnapshot
- LearningNotebookPack
- LearningArtifact
- Quiz hooks

Kabul:

- "Ne calisacagim?" sorusuna cevap verir.
- Aktif hatirlama ve mini kontrol icerir.

### 4.3 Quiz

Amac:

- Bilgi durumunu olcmek ve learning evidence uretmek.

Model:

- Quiz / Diagnostic

Sistem baglantilari:

- QuizRun
- QuizAttempt
- QuizAttemptRecorder
- LearningSignalService
- KnowledgeTracingState
- ConceptMastery

Kabul:

- Dogru cevap pre-submit sizmaz.
- Chat icinde cevap verilirse observed evidence kaydedilir.
- Verified mastery sadece gercek dogrulanmis cevapla artar.

### 4.4 Flashcard

Amac:

- Tekrari aktif hatirlama formatina cevirmek.

Model:

- Tutor/Summarizer

Baglantilar:

- Review due
- SRS
- Learning signals
- Weak concept queue

Kabul:

- Kisa, olculebilir, tek kavram odakli olur.

### 4.5 Slide Studio

Amac:

- Ders/kaynak icerigini sunum taslagina cevirmek.

Model:

- Summarizer + Diagram support

Artifact:

- `slide_deck_outline`

Alanlar:

- Slide title
- Bullets
- Speaker notes
- Checkpoint question
- Visual suggestion
- Source/Wiki label

Kabul:

- Speaker notes ve checkpoint sorulari bulunur.
- Export preview context metadata tasir.

### 4.6 Diagram / UML Studio

Amac:

- Kavram, surec, veri modeli, algoritma veya kaynak iliskisini Mermaid/UML olarak gostermek.

Destek tipleri:

- Mind map
- Flowchart
- Sequence diagram
- Class diagram
- State diagram
- ER diagram

Model:

- Diagram generator / Summarizer

Kabul:

- Diagram context'i surface disina cikmaz.
- Mermaid guvenli render/sanitize akisindan gecer.

### 4.7 Properties / Tags / Backlinks / Mentions

Amac:

- Wiki ve OrkaLM'i linked knowledge sistemi gibi calistirmak.

Wiki:

- Page properties
- Wiki tags
- Wiki backlinks
- Wiki linked mentions
- Wiki block refs

OrkaLM:

- Source properties
- Source tags
- Source backlinks
- Source linked mentions
- Source chunk refs

Kabul:

- Graph'lar ayri kalir.
- Cross-surface sync kapali gorunur.

### 4.8 Search / Templates / Export

Amac:

- Kullaniciya arama, filtreleme, tekrar kullanilabilir template ve guvenli export preview vermek.

Kabul:

- Wiki search Wiki sonuc doner.
- OrkaLM search source sonuc doner.
- Export context metadata tasir.

### 4.9 Audio Overview

Amac:

- Ders/kaynak icerigini konusmali sesli tekrar formatina cevirmek.

Modlar:

- Brief
- Deep dive
- Critique
- Debate

Kalite:

- Transcript
- Caption track
- Backend TTS
- Browser TTS fallback
- Retention/purge metadata

Kabul:

- `surface` dogru gelir.
- `wikiPageId` yalniz Wiki'de, `sourceId` yalniz OrkaLM'de olur.
- `crossSurfaceSync=false`.

### 4.10 Sesli Calisma Odasi

Amac:

- Audio dinlerken "burayi anlamadim" diyebilmek.

Baglantilar:

- AudioOverviewJob
- ClassroomSession
- ClassroomInteraction
- ActiveSegment
- Browser/Backend TTS

Kabul:

- Session context'i audio job context'iyle uyusmazsa hard fail.
- Public payload raw transcript/source chunk sizdirmaz.

## 5. API ve Artifact Haritasi

| Alan | Endpoint/Service | Ana DTO/Entity |
|---|---|---|
| Auth | `/api/auth/*` | AuthResponse, RefreshTokenCookie |
| Tutor | `/api/chat`, `/api/tutor/*` | TutorTurnState, TutorTrace |
| Wiki | `/api/wiki/*` | WikiPage, WikiBlock, WikiGraph |
| OrkaLM | `/api/sources/*` | LearningSource, SourceChunk |
| Notebook Studio | `/api/notebook-studio/*` | LearningNotebookPack, LearningArtifact |
| Audio | `/api/audio/overview` | AudioOverviewJobDto |
| Study room | `/api/classroom/*` | ClassroomSession, ClassroomInteraction |
| Quiz | `/api/quiz/*` | QuizRun, QuizAttempt |
| Assessment | `/api/assessment/*` | AssessmentBlueprint |
| Plan quality | `/api/plan-quality/*` | PlanQualityEvaluation |
| Learning signal | `/api/learning/signal` | LearningSignal |

## 6. Test ve Kanit Haritasi

| Kanit | Durum |
|---|---|
| API full test | 634/634 |
| Infrastructure full test | 176/176 |
| Frontend typecheck | Passed |
| Frontend build | Passed |
| Smoke UI | Passed |
| Smoke security | Passed |
| Smoke contracts | Passed |
| Notebook Studio e2e | 2/2 |
| Full Playwright | 5 passed / 1 skipped |

## 7. Ozelliklerin Gelecek Evrimi

Kisa vade:

- Life-proof browser test aktiflestirme
- Audio voice kalitesi/studio preset
- Question count pedagogy review
- Release packaging

Orta vade:

- Manual OrkaLM -> Wiki push
- Manual Wiki -> OrkaLM attach
- Video overview
- Teacher dashboard
- Curriculum marketplace

Uzun vade:

- Institutional analytics
- Offline vault export
- Multimodal source ingestion
- AI evaluation dashboard
- Mobile/audio-first study room

