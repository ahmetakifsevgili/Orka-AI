import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const srcRoot = path.join(root, "src");
const repoRoot = path.resolve(root, "..");

const checks = [];
const failures = [];

function read(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

function readRepo(relativePath) {
  return fs.readFileSync(path.join(repoRoot, relativePath), "utf8");
}

function addCheck(name, pass, detail = "") {
  checks.push({ name, pass, detail });
  if (!pass) failures.push(`${name}${detail ? `: ${detail}` : ""}`);
}

function walk(dir) {
  const output = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) output.push(...walk(full));
    else if (/\.(tsx?|css|mjs|js)$/.test(entry.name)) output.push(full);
  }
  return output;
}

const mojibakeContinuation =
  "(?:[\\u0080-\\u00BF]|\\u20AC|\\u201A|\\u0192|\\u201E|\\u2026|\\u2020|\\u2021|\\u02C6|\\u2030|\\u0160|\\u2039|\\u0152|\\u017D|\\u2018|\\u2019|\\u201C|\\u201D|\\u2022|\\u2013|\\u2014|\\u02DC|\\u2122|\\u0161|\\u203A|\\u0153|\\u017E|\\u0178)";
const mojibake = new RegExp(
  [
    `[\\u00C2-\\u00C5]${mojibakeContinuation}`,
    `\\u00E2${mojibakeContinuation}{1,2}`,
    "\\uFFFD",
  ].join("|")
);
const dirty = [srcRoot, path.join(root, "scripts")]
  .flatMap((sourceRoot) => fs.existsSync(sourceRoot) ? walk(sourceRoot) : [])
  .filter((file) => mojibake.test(fs.readFileSync(file, "utf8")));
addCheck("Turkish mojibake guard", dirty.length === 0, dirty.map((file) => path.relative(root, file)).join(", "));

const landing = read("src/pages/Landing.tsx");
const login = read("src/pages/Login.tsx");
addCheck("Landing has real ORKA module copy", landing.includes("Öğrenci sinyali yakalandı") && landing.includes("NotebookLM") && landing.includes("QA ve sistem güveni"));
addCheck("Landing keeps P4 product depth", landing.includes("Plan") && landing.includes("Wiki") && landing.includes("Quiz") && landing.includes("Sesli Ders") && landing.includes("IDE"));
addCheck("Release copy avoids certainty claims", !landing.includes("garanti alt") && !login.includes("%100 uyumlu") && !landing.includes("success guarantee") && !login.includes("official curriculum"));

const css = read("src/index.css");
addCheck("Mist Comfort utilities exist", css.includes(".orka-surface") && css.includes(".orka-panel") && css.includes(".orka-focus"));

const viteConfig = fs.readFileSync(path.join(root, "vite.config.ts"), "utf8");
addCheck("Vite production build warnings are bounded", viteConfig.includes("chunkSizeWarningLimit") && !viteConfig.includes("manualChunks"));
addCheck("Vite proxy targets active backend port", viteConfig.includes("localhost:5065") && viteConfig.includes("VITE_API_PROXY_TARGET"));

const ide = read("src/components/InteractiveIDE.tsx");
addCheck("IDE Turkish action copy", ide.includes("Kodu Çalıştır") && ide.includes("Hocaya Gönder"));
addCheck("IDE carries topic/session metadata", ide.includes("topicId") && ide.includes("sessionId") && ide.includes("IdeRunCompleted"));
addCheck("IDE sends learning signal to tutor", ide.includes("IdeSentToTutor") && ide.includes("LearningAPI.recordSignal"));
addCheck("IDE run output combines stdout and stderr", ide.includes("formatRunOutput") && ide.includes("Çıktı:") && ide.includes("Hata:"));
addCheck("IDE frontend run failures create learning signal", ide.includes("frontend-api-failure") && ide.includes("signalType: \"IdeRunCompleted\""));
addCheck("IDE tutor payload clips long output", ide.includes("MAX_TUTOR_OUTPUT_CHARS") && ide.includes("clipForTutor(outputText)"));

const classroom = read("src/components/ClassroomAudioPlayer.tsx");
addCheck("Classroom speaker guard", classroom.includes("ASISTAN") && classroom.includes("KONUK") && classroom.includes("repairLikelyMojibake"));
addCheck("Classroom assistant never disappears", classroom.includes("ensureClassroomDialogue") && classroom.includes("!hasAssistant"));

const api = read("src/services/api.ts");
const types = read("src/lib/types.ts");
const apiSmokeFactory = readRepo("Orka.API.Tests/ApiSmokeFactory.cs");
const quizLearningPipeline = readRepo("Orka.API.Tests/QuizLearningPipelineTests.cs");
const wikiGraphContract = readRepo("Orka.API.Tests/WikiGraphContractTests.cs");
const pedagogicalReleaseClosure = readRepo("Orka.API.Tests/PedagogicalReleaseClosureTests.cs");
const startApiScript = readRepo("scripts/start-api.ps1");
addCheck("Provider-free learning smoke proof is wired", apiSmokeFactory.includes("SmokeAgentFactory") && apiSmokeFactory.includes("UseInMemoryDatabase") && startApiScript.includes("InMemoryDatabase") && quizLearningPipeline.includes("WrongQuizAttempt_ReturnsSafeMisconceptionAndRemediationSeed") && wikiGraphContract.includes("WikiLearningTraceWriter"));
addCheck("Final pedagogical release harness is wired", pedagogicalReleaseClosure.includes("ProviderFreeLearningLoop_ConnectsPedagogicalProductizationSurfaces") && pedagogicalReleaseClosure.includes("TutorActionPlanner") && pedagogicalReleaseClosure.includes("/api/wiki/page/") && pedagogicalReleaseClosure.includes("/api/notebook-studio/wiki-page/") && pedagogicalReleaseClosure.includes("/api/sources/topic/") && pedagogicalReleaseClosure.includes("AssertNoPublicLeak"));
addCheck("Learning signal API exposed", api.includes("/learning/signal") && api.includes("recordSignal"));
addCheck("Learning snapshot API exposed", api.includes("LearningSnapshotsAPI") && api.includes("/learning-snapshots/active-lesson") && api.includes("/learning-snapshots/student-context"));
addCheck("Plan quality API exposed", api.includes("PlanQualityAPI") && api.includes("/plan-quality/topic/") && api.includes("/plan-quality/evaluate") && api.includes("PlanQualityEvaluationDto"));
addCheck("Learning quality API exposed", api.includes("/learning-quality/topic") && api.includes("getTopicQuality"));
addCheck("Assessment calibration API exposed", api.includes("/assessment/topic") && api.includes("runCalibration"));
addCheck("Assessment blueprint and quality API exposed", api.includes("buildPlanStepBlueprint") && api.includes("/assessment/blueprint/plan-step") && api.includes("/assessment/quality/evaluate") && types.includes("AssessmentBlueprintDto") && types.includes("AssessmentQualityEvaluationDto"));
addCheck("Quiz learning impact metadata is typed safely", types.includes("QuizResultLearningImpactDto") && types.includes("assessmentMode") && types.includes("misconceptionConfidence") && types.includes("nextTutorMove") && !types.includes("rawEvaluatorPayload") && !types.includes("rawProviderPayload"));
addCheck("Adaptive quiz API exposed", api.includes("/quiz/adaptive/start") && api.includes("getAdaptiveNext") && api.includes("answerAdaptive"));
addCheck("Tutor timeline API exposed", api.includes("/tutor/events/session") && api.includes("getSessionTimeline"));
addCheck("Standards alignment API exposed", api.includes("/standards/topic") && api.includes("StandardsAPI"));
addCheck("Exam framework API exposed", api.includes("ExamsAPI") && api.includes("/exams") && api.includes("/exams/import-tree") && api.includes("ExamDefinitionDto") && api.includes("ExamTreeImportDto"));
addCheck("Question bank API exposed", api.includes("QuestionsAPI") && api.includes("/questions") && api.includes("/submit-review") && api.includes("/publish") && api.includes("QuestionItemDto") && api.includes("CreateQuestionDto"));
addCheck("Question import API exposed", api.includes("QuestionImportsAPI") && api.includes("/question-imports/preview") && api.includes("/question-imports/approve") && api.includes("QuestionImportPreviewDto"));
addCheck("Question draft API exposed", api.includes("QuestionDraftsAPI") && api.includes("/question-drafts/preview") && api.includes("/question-drafts/approve") && api.includes("QuestionDraftPreviewDto"));
addCheck("Central exams API exposed", api.includes("CentralExamsAPI") && api.includes("/central-exams/kpss") && api.includes("/central-exams/kpss/turkce-paragraf/start") && api.includes("CentralExamStudyHomeDto") && api.includes("PracticeSessionDto"));
addCheck("Question quality analytics API exposed", api.includes("QuestionQualityAPI") && api.includes("/question-quality/questions/") && api.includes("/question-quality/central-exams/") && api.includes("QuestionItemAnalyticsDto") && api.includes("CentralExamBlueprintCoverageDto"));
addCheck("Production readiness API exposed", api.includes("/production-readiness/v1") && api.includes("ProductionReadinessAPI"));
addCheck("Tool capability API exposed", api.includes("/tools/capabilities") && api.includes("ToolsAPI"));
addCheck("Tool runtime API exposed", api.includes("/tools/runtime/traces") && api.includes("/tools/runtime/governance-summary") && api.includes("/tools/runtime/decide") && api.includes("ToolRuntimeTrace"));
addCheck("Learning runtime telemetry API exposed", api.includes("LearningRuntimeAPI") && api.includes("/learning-runtime/traces") && api.includes("/learning-runtime/health") && api.includes("/learning-runtime/privacy-check") && types.includes("LearningRuntimeHealthDto"));
addCheck("Agentic trust API exposed", api.includes("AgenticTrustAPI") && api.includes("/agentic-trust/check/user-message") && api.includes("/agentic-trust/check/public-payload") && api.includes("/agentic-trust/summary") && types.includes("AgenticTrustCheckResultDto") && types.includes("issuesByCategory"));
addCheck("Learning artifact lifecycle API exposed", api.includes("LearningArtifactsAPI") && api.includes("/learning-artifacts") && api.includes("/learning-artifacts/validate") && api.includes("refresh-status") && types.includes("LearningArtifactDto") && types.includes("sourceBasis") && types.includes("accessibility"));
addCheck("Learning artifact contract stays safe", types.includes("safeContent") && types.includes("LearningArtifactSafetyDto") && !types.includes("rawProviderPayload") && !types.includes("rawToolPayload") && !types.includes("rawSourceChunk"));
addCheck("Notebook Studio API exposed", api.includes("NotebookStudioAPI") && api.includes("/notebook-studio/topic/") && api.includes("/milestone-pack") && api.includes("/artifact") && types.includes("LearningNotebookPackDto") && types.includes("NotebookStudioNextActionDto"));
addCheck("OrkaLM source notebook API exposed", api.includes("getTopicNotebook") && api.includes("/sources/topic/") && api.includes("/notebook") && api.includes("buildSourcePack") && api.includes("/notebook-studio/sources/") && types.includes("SourceNotebookDto") && types.includes("sourceSurface"));
addCheck("OrkaLM source-concept graph API exposed", api.includes("getSourceConceptLinks") && api.includes("syncSourceConceptLinks") && api.includes("/concept-links/sync") && api.includes("getTopicSourceConceptGraph") && api.includes("/concept-graph") && api.includes("getWikiPageSourceLinks") && types.includes("SourceConceptGraphDto") && types.includes("SourceConceptLinkSummaryDto"));
addCheck("OrkaLM ask-source API exposed", api.includes("askTopicSources") && api.includes("askSources") && api.includes("/sources/ask") && api.includes("/ask") && types.includes("SourceQuestionRequestDto") && types.includes("SourceQuestionResponseDto") && types.includes("SourceQuestionCitationDto"));
addCheck("OrkaLM multi-source compare API exposed", api.includes("compareTopicSources") && api.includes("/compare") && api.includes("getTopicCitationReview") && api.includes("/citation-review") && types.includes("MultiSourceCompareResultDto") && types.includes("CitationReviewItemDto"));
addCheck("OrkaLM source Q&A thread API exposed", api.includes("listQuestionThreads") && api.includes("createQuestionThread") && api.includes("askQuestionThread") && api.includes("reviewQuestionThread") && api.includes("writeQuestionThreadWikiTrace") && api.includes("/sources/question-threads") && types.includes("SourceQuestionThreadDto") && types.includes("SourceQuestionTurnDto"));
addCheck("OrkaLM source study summary API exposed", api.includes("getSourceStudySummary") && api.includes("/sources/study-summary") && types.includes("SourceStudySummaryDto") && types.includes("recommendedNextAction"));
const workspaceHook = read("src/hooks/useLearningWorkspaceState.ts");
addCheck("Learning workspace state helper exists", workspaceHook.includes("useLearningWorkspaceState") && workspaceHook.includes("Promise.all") && workspaceHook.includes("LearningSnapshotsAPI.getActiveLesson") && workspaceHook.includes("PlanQualityAPI.getLatest") && workspaceHook.includes("TutorAPI.getTopicPolicy") && workspaceHook.includes("LearningArtifactsAPI.list") && workspaceHook.includes("NotebookStudioAPI.listPacks") && workspaceHook.includes("ToolsAPI.getGovernanceSummary") && workspaceHook.includes("LearningRuntimeAPI.getHealth"));
addCheck("Learning workspace state degrades safely", workspaceHook.includes("quiet(") && workspaceHook.includes("catch") && workspaceHook.includes("staleWarnings") && workspaceHook.includes("safetyWarnings"));
addCheck("Learning APIs exposed", api.includes("FlashcardsAPI") && api.includes("ReviewAPI") && api.includes("DailyChallengeAPI") && api.includes("BookmarksAPI"));
addCheck("Plan diagnostic has explicit intent gate API", api.includes("analyzePlanIntent") && api.includes("/quiz/plan-diagnostic/intent"));
addCheck("Stream APIs use authenticated fetch wrapper", api.includes("export const authenticatedFetch") && api.includes("ChatAPI") && api.includes('authenticatedFetch("/api/chat/stream"') && api.includes('authenticatedFetch("/api/korteks/research-stream"') && api.includes('authenticatedFetch("/api/korteks/research-file"'));
addCheck("Stream APIs never send null bearer tokens", !api.includes("Bearer null") && !api.includes("Bearer undefined"));
addCheck("Auth fetch and axios requests include refresh cookie credentials", api.includes("withCredentials: true") && api.includes('credentials: init.credentials ?? "include"'));
addCheck("Auth logout API and scoped cleanup are exposed", api.includes("logout: () =>") && api.includes("/auth/logout") && api.includes("storage.clear") && !api.includes("localStorage.clear()"));
addCheck("Dashboard coordination contract is typed", api.includes("coordinationScope?:") && api.includes("coordinationHealth?:") && api.includes("activeLessonTopicId"));
addCheck("Korteks sync and stream contracts are separate", api.includes("KorteksSyncResponseDto") && api.includes("researchSync") && api.includes("/api/korteks/research-stream"));
addCheck("Korteks synthesis contract exposed", api.includes("synthesisWorkflowId") && api.includes("getLatestSynthesis") && api.includes("/korteks/synthesis/latest") && types.includes("KorteksResearchWorkflow"));
addCheck("Auth cleanup is scoped to Orka keys", api.includes("storage.clear") && !api.includes("localStorage.clear()"));

const toolContext = read("src/contexts/ToolCapabilitiesContext.tsx");
addCheck("Tool capability context drives visibility", toolContext.includes("ToolsAPI.getCapabilities") && toolContext.includes("isVisibleForUser"));

const dashboard = read("src/components/DashboardPanel.tsx");
addCheck("Learning signal book visible", dashboard.includes("Öğrenci Sinyal Defteri") && dashboard.includes("learningSignalBook"));
addCheck("Learning memory profile summary is visible", dashboard.includes("Orka’nın öğrenci profili") && dashboard.includes("Güçlü ilerlediğin alanlar") && dashboard.includes("Tekrar gerektiren alanlar") && dashboard.includes("Henüz yeterli öğrenme sinyali yok. Quiz, chat ve Wiki kullandıkça profil oluşur."));
addCheck("Learning memory stays status-only beside weak queue", dashboard.includes("StudentProfileSummary") && dashboard.includes("Önerilen telafi odağı") && dashboard.includes("Planner için güvenli öğrenme girdileri hazırlanıyor"));
addCheck("Adaptive study planner is visible", dashboard.includes("Çalışma planı") && dashboard.includes("Neden bu adım?") && dashboard.includes("Planı hedefe göre güncelle"));
addCheck("Adaptive goal preview uses safe exam and career copy", dashboard.includes("Bu plan mevcut konu ağına") && dashboard.includes("İşe giriş garantisi değildir") && api.includes("previewAdaptiveStudyPlan") && api.includes("/dashboard/adaptive-study-plan"));
addCheck("Diagnostic intake copy is user-facing", dashboard.includes("Kısa seviye tespiti") && dashboard.includes("kullanıcı beyanı tek gerçek kabul edilmez"));
addCheck("Dashboard guidance and coordination visibility exist", dashboard.includes("Sıradaki en iyi adım") && dashboard.includes("Koordinasyon özeti") && dashboard.includes("coordinationHealth"));
addCheck("Learning guidance pack surfaces are visible", dashboard.includes("Çalışma kuyruğu") && dashboard.includes("Eksiklerini tamamla") && dashboard.includes("Kaldığın dersten devam et") && dashboard.includes("buildWeakConceptActionQueue"));
addCheck("Coordination health uses user-facing labels", dashboard.includes("Kaynaklar hazır") && dashboard.includes("Wiki eksik olabilir") && dashboard.includes("Quiz kanıtı zayıf") && dashboard.includes("RAG kaynak kalitesi iyi"));
addCheck("Dashboard source coverage coach is visible", dashboard.includes("Kaynak kapsaması") && dashboard.includes("Bu konuda kaynak eksik olabilir.") && dashboard.includes("Kaynak kalitesi zayıf; yeni kaynak eklemek faydalı olabilir.") && dashboard.includes("deriveSourceCoverageCoach"));

const healthHud = read("src/components/SystemHealthHUD.tsx");
addCheck("Admin HUD shows learning bridges", healthHud.includes("learningBridge") && healthHud.includes("Agent bridge monitor"));
const runtimeWorkspace = read("src/components/AgenticWorkspace.tsx");
addCheck("Agentic workspace shows safe runtime health", runtimeWorkspace.includes("Runtime sagligi") && runtimeWorkspace.includes("runtimeHealth") && runtimeWorkspace.includes("fallbackCount") && !runtimeWorkspace.includes("rawProviderPayload") && !runtimeWorkspace.includes("rawToolPayload"));

const wiki = read("src/components/WikiMainPanel.tsx");
const notebookStudio = read("src/components/NotebookStudioPanel.tsx");
addCheck("Wiki actions create learning signals", wiki.includes("recordWikiAction") && wiki.includes("WikiActionClicked"));
addCheck("Wiki remains active while OrkaLM reuses notebook source surface", wiki.includes('mode?: "wiki" | "orkalm"') && wiki.includes("OrkaLM Kaynak Notebook") && wiki.includes("Notebook Kaynak"));
addCheck("OrkaLM dedicated source notebook UI visible", wiki.includes("OrkaLM source notebook") && wiki.includes("activeSourceNotebook") && wiki.includes("sourceNotebook") && notebookStudio.includes("Source Pack") && notebookStudio.includes("source_notebook"));
addCheck("OrkaLM source-to-concept graph UI visible", wiki.includes("Source-to-concept graph") && wiki.includes("sourceConceptLinks") && wiki.includes("sourceConceptGraph") && wiki.includes("handleSyncSourceConceptLinks") && wiki.includes("Link sync"));
addCheck("Wiki concept supporting-source UI visible", wiki.includes("Supporting sources") && wiki.includes("activePageSourceLinks") && wiki.includes("getWikiPageSourceLinks"));
addCheck("OrkaLM ask-source UX visible", wiki.includes("handleAskSource") && wiki.includes("handleAskSourceCollection") && wiki.includes("Ask selected source") && wiki.includes("Ask source collection") && wiki.includes("Citation chips") && wiki.includes("Related concept pages") && wiki.includes("sourceQuestionResponse"));
addCheck("OrkaLM multi-source compare UX visible", wiki.includes("handleCompareSources") && wiki.includes("Multi-source compare") && wiki.includes("Compare selected") && wiki.includes("Shared linked concepts") && wiki.includes("Citation review") && wiki.includes("semantic agreement iddiasi uretmez"));
addCheck("OrkaLM source Q&A memory UX visible", wiki.includes("Source Q&A memory") && wiki.includes("Save source Q&A thread") && wiki.includes("Ask follow-up") && wiki.includes("Mark needs review") && wiki.includes("Write to Wiki") && wiki.includes("activeQuestionThread"));
addCheck("OrkaLM source study summary UX visible", wiki.includes("Source study status") && wiki.includes("sourceStudySummary") && wiki.includes("recommendedNextAction") && wiki.includes("citationWarningCount") && wiki.includes("linked concepts"));
addCheck("OrkaLM ask-source UI is payload-safe", !wiki.includes("rawProviderPayload") && !wiki.includes("rawSourceChunk") && !wiki.includes("hiddenPrompt") && !wiki.includes("correctAnswer") && !wiki.includes("chunk.text.slice") && !wiki.includes("{chunk.text}"));
addCheck("OrkaLM compare UI avoids fake agreement claims", !wiki.includes("semantic agreement") || wiki.includes("semantic agreement iddiasi uretmez"));
addCheck("Notebook tools refresh after source activity", wiki.includes("notebookRefreshTick") && wiki.includes("setNotebookRefreshTick"));
addCheck("Wiki source graph visible", wiki.includes("Kaynak graf") && wiki.includes("sourceGraph"));
addCheck("Wiki source evidence panel visible", wiki.includes("Kaynak Kan") && wiki.includes("sourceCitations") && wiki.includes("handleSourcePageNav"));
addCheck("Wiki source viewer avoids raw chunk rendering", wiki.includes("Raw kaynak parçası burada gösterilmez") && !wiki.includes("{chunk.text}</p>") && !wiki.includes("chunk.text.slice"));
addCheck("Wiki source evidence trust strip visible", wiki.includes("source-evidence-trust-strip") && wiki.includes("Citation trail") && wiki.includes("Kaynak güveni"));
addCheck("Wiki citation chips expose scope summaries", wiki.includes("citationScopeSummary") && wiki.includes("citationDisplayTitle") && wiki.includes("citationPrimaryLabel"));
addCheck("Wiki source coverage coach is visible", wiki.includes("Kaynak kapsaması") && wiki.includes("Bu konu için kaynaklar hazır.") && wiki.includes("RAG yanıtları için yeterli kaynak bulunamayabilir") && wiki.includes("buildWikiSourceCoverageCoach"));
addCheck("Wiki learning trace summary is user-facing", wiki.includes("WikiLearningTraceSummary") && wiki.includes("Orka bu turda") && wiki.includes("Bu cevap kaynaklarla desteklendi.") && wiki.includes("Quiz/pratik kanıtı güncellendi."));
addCheck("Wiki study pack entry is visible", wiki.includes("Wiki Çalışma Paketi") && wiki.includes("Bu konuyu çalış") && wiki.includes("Özeti oku") && wiki.includes("Kavramları gözden geçir") && wiki.includes("Kartlarla çalış"));
addCheck("Wiki weak queue context is visible", wiki.includes("Bu konuda çalışma kuyruğu sinyali var.") && wiki.includes("Zayıf kavramı Wiki’den tekrar edebilir") && wiki.includes("wiki-study-reinforcement"));
addCheck("Wiki learning workspace state is synchronized", wiki.includes("useLearningWorkspaceState") && wiki.includes("Learning workspace") && wiki.includes("workspaceState.recentArtifacts") && wiki.includes("workspaceState.sourceReadiness"));
addCheck("Wiki Vault page tree and filters are visible", wiki.includes("Wiki Vault") && wiki.includes("Page tree / list") && wiki.includes("wikiVaultQuery") && wiki.includes("wikiVaultFilter") && wiki.includes("Sayfa, concept veya kaynak ara"));
addCheck("Wiki Vault graph context is visible", wiki.includes("Backlinks / local graph") && wiki.includes("Geri linkler") && wiki.includes("Cikis linkleri") && wiki.includes("Local komsular") && wiki.includes("activeBacklinks") && wiki.includes("activeOutgoingLinks"));
addCheck("Wiki Vault block grouping is visible", wiki.includes("Blok gruplari") && wiki.includes("blockGroupFor") && wiki.includes("Ogrenci sorulari") && wiki.includes("Takilma ve onarim"));
addCheck("Wiki Vault page-aware Notebook context is preserved", wiki.includes("NotebookStudioPanel") && wiki.includes("wikiPageId={isOrkaLm ? undefined : activePage?.id}") && wiki.includes("wikiPageTitle={isOrkaLm ? undefined : activePage?.title}"));
addCheck("Wiki Copilot page-aware UX is visible and safe", wiki.includes("wiki-copilot-panel") && wiki.includes("Wiki Copilot") && wiki.includes("handleWikiCopilotAction") && api.includes("getPageCopilot") && types.includes("WikiCopilotContextDto") && !wiki.includes("rawToolPayload") && !wiki.includes("rawSourceChunk") && !wiki.includes("answerKey"));
addCheck("Notebook Studio panel is wired into Wiki", wiki.includes("NotebookStudioPanel") && notebookStudio.includes("Milestone Pack") && notebookStudio.includes("audio_overview") && notebookStudio.includes("mind_map") && notebookStudio.includes("review_quiz"));
addCheck("Notebook Studio production states are visible", notebookStudio.includes("Kaynak hazirlik") && notebookStudio.includes("Notebook Studio paketleri yuklenemedi") && notebookStudio.includes("script-only") && notebookStudio.includes("Henuz cikti yok."));
addCheck("Notebook Studio advanced media/export actions are visible", notebookStudio.includes("video_ready_package") && notebookStudio.includes("slide_export_manifest") && notebookStudio.includes("audio_transcript") && notebookStudio.includes("caption_track") && notebookStudio.includes("Export readiness"));
addCheck("Notebook Studio media copy avoids fake export claims", notebookStudio.includes("Video-ready paket") && notebookStudio.includes("Slide export manifest") && !notebookStudio.includes("PPTX indir") && !notebookStudio.includes("Video hazir"));
addCheck("Notebook Studio slide export contract is wired", api.includes("getExportPreview") && api.includes("/export/preview") && api.includes("exportPack") && api.includes("/export") && types.includes("NotebookExportResultDto") && types.includes("NotebookSlideExportPreviewDto"));
addCheck("Notebook Studio export UX is honest", notebookStudio.includes("Slide export paketi") && notebookStudio.includes("pptx_not_enabled") && notebookStudio.includes("Safe HTML") && !/fake video|fake pptx|pptx indir/i.test(notebookStudio));
addCheck("Notebook Studio export preview is useful", notebookStudio.includes("Slayt listesi") && notebookStudio.includes("Kaynak temeli") && notebookStudio.includes("Erisilebilirlik") && notebookStudio.includes("Checkpoint var") && notebookStudio.includes("Speaker notes var"));
addCheck("Notebook Studio PPTX disabled copy is explicit", notebookStudio.includes("PPTX etkin degil") && notebookStudio.includes("escaped HTML") && !notebookStudio.includes("PPTX hazir") && !notebookStudio.includes("PPTX indir"));
addCheck("Notebook Studio UI stays payload-safe", !notebookStudio.includes("rawProviderPayload") && !notebookStudio.includes("rawSourceChunk") && !notebookStudio.includes("hiddenPrompt") && !notebookStudio.includes("correctAnswer"));
const notebookMojibake = new RegExp("[\\u00c2\\u00c3\\u00c4\\u00c5\\ufffd]");
addCheck("Notebook Studio UI copy is readable", !notebookMojibake.test(notebookStudio) && notebookStudio.includes("Calisma rehberi") && notebookStudio.includes("Zayif alan") && notebookStudio.includes("Sesli anlatim"));

const sidebar = read("src/components/LeftSidebar.tsx");
addCheck("Topic readiness badges are visible", sidebar.includes("TopicReadinessBadge") && sidebar.includes("Hazır") && sidebar.includes("Dikkat") && sidebar.includes("Yeni") && sidebar.includes("getTopicReadinessBadge"));
addCheck("App sidebar exposes logout action", sidebar.includes("Çıkış yap") && sidebar.includes("onLogout") && sidebar.includes("logoutLoading") && sidebar.includes("LogOut"));

const centralExams = read("src/components/CentralExamsPanel.tsx");
const centralHome = read("src/pages/Home.tsx");
addCheck("Central exams product shell is visible", sidebar.includes("Merkezi Sınavlar") && centralHome.includes("central-exams") && centralExams.includes("Merkezi Sinavlar") && centralExams.includes("KPSS hazirlik iskeleti"));
addCheck("Central exams safe KPSS copy is guarded", centralExams.includes("Resmi mufredat iddiasi degildir") && centralExams.includes("dogrulanmis kaynak") && !centralExams.includes("kazanma garantisi") && !centralExams.includes("official curriculum complete") && !centralExams.includes("NotebookLM"));

addCheck("Central exams mini deneme stays safe", centralExams.includes("Mini Deneme") && centralExams.includes("Resmi OSYM simulasyonu degil") && !centralExams.includes("puan tahmini") && !centralExams.includes("percentile") && !centralExams.includes("dershane paneli") && !centralExams.includes("scraping"));
addCheck("Central exams multi-exam shell is visible", centralExams.includes("YKS") && centralExams.includes("LGS") && centralExams.includes("YDS") && centralExams.includes("Hazirlik iskeleti") && centralExams.includes("Pratik") && centralExams.includes("Mini deneme"));
addCheck("Central exams scaffold copy stays safe", !centralExams.includes("resmi kapsam tamam") && !centralExams.includes("kazanma garantisi") && !centralExams.includes("official MEB simulation") && !centralExams.includes("official OSYM simulation") && !centralExams.includes("NotebookLM"));
addCheck("KPSS pilot practice flow is visible", centralExams.includes("KPSS Turkce Paragraf pratigi") && centralExams.includes("Pratigi baslat") && centralExams.includes("Cevaplari gonder") && centralExams.includes("Cevap anahtari ve aciklama sadece gonderimden sonra gosterilir"));
addCheck("KPSS pilot practice hides pre-submit answer keys", centralExams.includes("Seceneklerde dogru cevap bilgisi tasinmaz") && centralExams.includes("result.correctOptionKey") && !centralExams.includes("option.isCorrect") && !centralExams.includes("storageKey"));

const citationDisplay = read("src/lib/citationDisplay.ts");
addCheck("Evidence quality helpers have user-facing labels", citationDisplay.includes("evidenceQualityLabel") && citationDisplay.includes("Kaynak güveni güçlü") && citationDisplay.includes("Kaynak güveni sınırlı") && citationDisplay.includes("Kaynak bulunamadı"));
addCheck("Citation scope metadata has user-facing labels", citationDisplay.includes("Bu ders") && citationDisplay.includes("Üst konu") && citationDisplay.includes("Alt ders") && citationDisplay.includes("Wiki ağacı"));

const richMarkdown = read("src/components/RichMarkdown.tsx");
addCheck("Citation clicks are observable", richMarkdown.includes("onCitationClick") && richMarkdown.includes("citationKind") === false);
addCheck("Mermaid stays lazy loaded", richMarkdown.includes('import("mermaid")') && !richMarkdown.includes('import mermaid from "mermaid"'));

const home = read("src/pages/Home.tsx");
addCheck("Toast import has no static/dynamic conflict", !home.includes('import("react-hot-toast")'));
addCheck("Learning panel is wired into app shell", home.includes("LearningPanel") && home.includes('"learning"'));

const learningPanel = read("src/components/LearningPanel.tsx");
addCheck("Learning panel uses durable backend surfaces", learningPanel.includes("FlashcardsAPI") && learningPanel.includes("ReviewAPI") && learningPanel.includes("DailyChallengeAPI") && learningPanel.includes("BookmarksAPI"));
addCheck("Adaptive practice is wired into learning panel", learningPanel.includes("startAdaptivePractice") && learningPanel.includes("adaptiveNext") && learningPanel.includes("QuizAPI.startAdaptive"));

const chatMessage = read("src/components/ChatMessage.tsx");
const chatPanel = read("src/components/ChatPanel.tsx");
addCheck("Chat learning trace shows evidence quality warnings", chatMessage.includes("evidenceQuality") && chatMessage.includes("evidenceQualityDetail") && chatMessage.includes("evidenceQualityLabel"));
addCheck("Tutor intelligence metadata is user-facing", chatMessage.includes("tutorResponseMode") && chatMessage.includes("personalizationMode") && chatMessage.includes("Kaynak güveni sınırlı; cevabı kontrol ederek kullan.") && chatMessage.includes("Anlatım seviyesi mevcut ilerlemene göre ayarlandı."));
addCheck("Tutor Socratic prompt stays visible", chatMessage.includes("Kendini kontrol et") && chatMessage.includes("nextCheckPrompt"));
addCheck("Chat metadata chips render additively", chatMessage.includes("ChatMetadataChips") && chatMessage.includes("usedTools") && chatMessage.includes("fallbackReason"));
addCheck("Chat citation chips expose scope summaries", chatMessage.includes("citationScopeSummary") && chatMessage.includes("citationDisplayTitle") && chatMessage.includes("citationPrimaryLabel"));
addCheck("Chat learning trace summary is user-facing", chatMessage.includes("LearningTraceSummaryLite") && chatMessage.includes("Orka bu turda") && chatMessage.includes("Bu cevap kaynaklarla desteklendi.") && chatMessage.includes("Henüz öğrenme izi oluşmadı."));
addCheck("Live tutor trace timeline is rendered", chatMessage.includes("LiveTutorTrace") && chatMessage.includes("TutorAPI.getSessionTimeline") && chatMessage.includes("Tutor izi"));
addCheck("Plan mode requires intent confirmation before learning research", chatPanel.includes("pendingPlanIntent") && chatPanel.includes("Onayla ve araştır") && chatPanel.includes("approvedResearchIntent"));
addCheck("Plan diagnostic preserves quality metadata", api.includes("conceptGraphQualityStatus") && chatPanel.includes("qualityReportId"));
addCheck("Plan quality metadata stays safe", types.includes("PlanStepContractDto") && types.includes("sequenceReason") && types.includes("quizHook") && types.includes("tutorHook") && types.includes("planSourceReadiness") && !types.includes("rawProviderPayload"));
addCheck("Plan mode exposes meaningful staged UX", chatPanel.includes("Niyet ayrılıyor") && chatPanel.includes("Bağlam taranıyor") && chatPanel.includes("Seviye testi kuruluyor") && chatPanel.includes("Öğrenme yolu üretiliyor"));

addCheck("Tutor response policy closure metadata is typed and rendered", types.includes("TutorResponsePolicyDto") && types.includes("tutorTeachingMove") && types.includes("tutorGroundingPolicy") && chatMessage.includes("tutorTeachingMove") && chatMessage.includes("tutorGroundingPolicy") && api.includes("/tutor/policy/evaluate") && api.includes("/tutor/next-actions"));
addCheck("Tutor tool decision metadata is typed and rendered safely", types.includes("TutorToolDecisionDto") && types.includes("tutorToolDecision") && chatMessage.includes("toolDecision?.selectedAction") && chatMessage.includes("toolDecision.safetyWarnings") && !chatMessage.includes("rawToolPayload") && !chatMessage.includes("rawSourceChunk"));
addCheck("Tutor lesson delivery rubric metadata is typed and rendered safely", types.includes("TutorLessonDeliveryDto") && types.includes("tutorLessonDelivery") && types.includes("deliveryMode") && chatMessage.includes("lessonDelivery?.deliveryMode") && chatMessage.includes("lessonDelivery.studentVisibleSummary") && !chatMessage.includes("rawProviderPayload") && !chatMessage.includes("rawSourceChunk"));
addCheck("Adaptive diagnostic and course-plan quality metadata is typed and rendered safely", types.includes("AdaptiveDiagnosticDto") && types.includes("CoursePlanQualityDto") && types.includes("adaptiveDiagnostic") && types.includes("coursePlanQuality") && chatMessage.includes("adaptiveDiagnostic?.planReadiness") && chatMessage.includes("coursePlanQuality?.readinessStatus") && !chatMessage.includes("rawProviderPayload") && !chatMessage.includes("rawSourceChunk"));
const agenticWorkspace = read("src/components/AgenticWorkspace.tsx");
addCheck("Frontend learning workspace state drives chat rail", chatPanel.includes("useLearningWorkspaceState") && chatPanel.includes("workspaceState.recentArtifacts") && chatPanel.includes("workspaceState={workspaceState}") && agenticWorkspace.includes("workspaceState?.currentPlanStep") && agenticWorkspace.includes("workspaceState?.sourceReadiness"));
addCheck("Frontend artifact canvas renders Pack 8 artifacts safely", agenticWorkspace.includes("learningArtifacts?: LearningArtifactDto[]") && agenticWorkspace.includes("artifact.safeContent") && agenticWorkspace.includes("safeMarkdownComponents") && agenticWorkspace.includes("artifact.sourceBasis") && agenticWorkspace.includes("artifact.accessibility"));

const packageJson = read("package.json");
const onboarding = read("src/components/PremiumOnboardingTour.tsx");
addCheck("P5 premium onboarding is wired", packageJson.includes('"driver.js"') && home.includes("usePremiumOnboarding") && onboarding.includes("tour-new-topic") && onboarding.includes("tour-nav-dashboard") && onboarding.includes("tour-nav-wiki") && onboarding.includes("tour-nav-ide"));

const languageContext = read("src/contexts/LanguageContext.tsx");
const languages = read("src/i18n/languages.ts");
const messages = read("src/i18n/messages.ts");
addCheck("First-wave language foundation exists", languages.includes('"pt-BR"') && languages.includes('"pl"') && messages.includes("landing_title_a") && languageContext.includes("normalizeLocale"));
addCheck("Legacy Turkish locale fallback avoids dirty literals", languages.includes("LEGACY_TURKISH_MOJIBAKE") && languages.includes(".map((code) => String.fromCharCode(code))") && languages.includes("0xc3"));
addCheck("Landing and app shell expose language selector", landing.includes("setLanguage") && landing.includes("languages.map") && sidebar.includes("languages.map") && sidebar.includes("interface_language"));
addCheck("OrkaLM source notebook is wired into app shell", home.includes('"sources"') && home.includes('mode="orkalm"') && sidebar.includes('labelKey: "sources"') && messages.includes("sources"));

const classroomPlayer = read("src/components/ClassroomAudioPlayer.tsx");
addCheck("Classroom active segment bridge", classroomPlayer.includes("activeSegment") && (classroomPlayer.includes("Anlamadım") || classroomPlayer.includes('t("confused")')));
addCheck("Classroom ask answer joins audio queue", classroomPlayer.includes("queuedLines") && classroomPlayer.includes("speakLine(startIndex"));
addCheck("P5 classroom backend audio fallback", api.includes("interactionId") && api.includes("getInteractionAudio") && classroomPlayer.includes("tryPlayBackendAudio") && classroomPlayer.includes("speechSynthesis"));

const quiz = read("src/components/QuizCard.tsx");
const quizParser = read("src/lib/quizParser.ts");
addCheck("Quiz raw JSON leak guard", !quiz.includes("JSON.stringify(quiz") && !quiz.includes("{JSON.stringify"));
addCheck("Quiz stays out of chat command flow", !quiz.includes("Quiz Cevab") && !quiz.includes("[SKIP_QUIZ]") && !quiz.includes("crypto.randomUUID") && quiz.includes("Quiz akışı tamamlandı"));
addCheck("Quiz pre-submit path does not depend on client answer key", !quiz.includes("option.isCorrect") && !quiz.includes("activeQuiz.explanation") && !quiz.includes("isCorrect: attempt.isCorrect") && !quiz.includes("explanation: activeQuiz.explanation") && !quizParser.includes("correctHint") && !quizParser.includes("isCorrect ="));
addCheck("Quiz feedback copy is pedagogical", quiz.includes("Tekrar edilmesi iyi olur") && quiz.includes("Bu cevap doğru değil") && !quiz.includes("Harika gidiyorsun"));
addCheck("Quiz wrong answer recovery CTA is visible", quiz.includes("Toparlanma adımı") && quiz.includes("Tutor’a sor") && quiz.includes("Wiki’de tekrar et") && quiz.includes("Benzer pratik çöz"));
addCheck("Pack 3 misconception remediation stays user-safe", quiz.includes("Yanılgı sinyali") && quiz.includes("Kanıt durumu") && dashboard.includes("remediationSeed") && chatMessage.includes("Yanılgı sinyali güvenli şekilde işlendi"));
addCheck("Pack A learning loop metadata surfaces in chat trace", chatMessage.includes("metadata?.misconceptionSignal") && chatMessage.includes("metadata?.learningSignalConfidence") && chatMessage.includes("metadata?.remediationSeed"));
addCheck("Pack 3 raw evaluator payload is not rendered", !quiz.includes("EvaluatorFeedback") && !dashboard.includes("EvaluatorFeedback") && !chatMessage.includes("EvaluatorFeedback") && !quiz.includes("evaluationScore") && !dashboard.includes("evaluationScore") && !chatMessage.includes("evaluationScore"));
addCheck("Quiz parser strips correctness labels from options", quizParser.includes("dogru") && quizParser.includes("yanlis") && quizParser.includes("incorrect"));
addCheck("Mermaid error SVG falls back safely", chatMessage.includes("looksLikeMermaidFailure") && chatMessage.includes("Mermaid returned an error SVG."));
addCheck("Favicon is present for runtime browser noise", fs.existsSync(path.join(root, "public", "favicon.ico")) && fs.statSync(path.join(root, "public", "favicon.ico")).size > 0);

const tutorAgent = readRepo("Orka.Infrastructure/Services/TutorAgent.cs");
const deepPlanAgent = readRepo("Orka.Infrastructure/Services/DeepPlanAgent.cs");
const planQualityTests = readRepo("Orka.API.Tests/PlanQualityGuardTests.cs");
const program = readRepo("Orka.API/Program.cs");
const studyIntentAnalyzer = readRepo("Orka.Infrastructure/Services/StudyIntentAnalyzer.cs");
const planDiagnostic = readRepo("Orka.Infrastructure/Services/PlanDiagnosticService.cs");
const v29QualityTests = readRepo("Orka.API.Tests/OrkaV29QualityRealityGateTests.cs");
addCheck("Backend StudyIntentAnalyzer is the plan gate", studyIntentAnalyzer.includes("StudyIntentAnalyzer") && studyIntentAnalyzer.includes("researchIntent") && planDiagnostic.includes("Approved study intent is required"));
addCheck("V2.9 quality reality gate is wired", v29QualityTests.includes("QualityScenarioCatalog") && v29QualityTests.includes("A01") && v29QualityTests.includes("E56") && v29QualityTests.includes("StudyIntentAnalyzer_ProducesApprovedResearchIntentQualitySignals"));
addCheck("P4 visual learning validator is action-plan aware", tutorAgent.includes("[P4 GÖRSEL ÖĞRENME VALIDATOR - ACTION PLAN ÖNCELİKLİ]") && tutorAgent.includes("Mermaid") && tutorAgent.includes("mikro kontrol sorusu"));
addCheck("P4 domain plan templates are removed", !deepPlanAgent.includes("PlanDomain.") && !deepPlanAgent.includes("BuildDomainFallbackModules") && deepPlanAgent.includes("BuildConceptGraphPlanningGuidance"));
addCheck("DeepPlan quality floor rejects thin plans", deepPlanAgent.includes("MinimumProgrammingTotalLessons = 24") && deepPlanAgent.includes("TryAcceptPlanModules") && deepPlanAgent.includes("Orka IDE/sandbox yalnizca uygun pratik"));
addCheck("Programming fallback is generic concept-graph based", deepPlanAgent.includes("BuildConceptGraphFallbackModules") && deepPlanAgent.includes("Onkosul Haritasi") && deepPlanAgent.includes("Mastery Kontrolu") && !deepPlanAgent.includes("BuildProgrammingFallbackModules") && !deepPlanAgent.includes("Orka IDE sandbox'ta ilk {subject} uygulamasi"));
addCheck("Tutor coding lessons default to Orka IDE", tutorAgent.includes("ORKA IDE VARSAYILAN ORTAMDIR") && tutorAgent.includes("harici kurulumları ilk adım gibi anlatma"));
addCheck("P4 language-specific plan template is removed", !deepPlanAgent.includes("[DOMAIN SABLONU - DIL OGRENIMI]") && !deepPlanAgent.includes("Spaced Repetition") && !deepPlanAgent.includes("Speaking Prompt") && deepPlanAgent.includes("GENERIC CONCEPT GRAPH"));
addCheck("P4 plan quality backend guard checks generic architecture", planQualityTests.includes("PlanQualityGuardTests") && planQualityTests.includes("DeepPlan_FallbackModulesComeFromGenericConceptGraph") && planQualityTests.includes("DeepPlan_NoLongerExposesDomainSpecificPlanningMode"));
const aiExtensions = readRepo("Orka.API/Extensions/AiProviderExtensions.cs");
addCheck("P5 YouTube transcript plugin is in SK bridge", aiExtensions.includes("YouTubeTranscriptPlugin") && aiExtensions.includes("AddFromObject(sp.GetRequiredService<YouTubeTranscriptPlugin>())"));

for (const check of checks) {
  const icon = check.pass ? "OK" : "FAIL";
  console.log(`${icon} ${check.name}${check.detail ? ` - ${check.detail}` : ""}`);
}

if (failures.length > 0) {
  console.error(`\nUI smoke failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log("\nUI smoke passed.");
