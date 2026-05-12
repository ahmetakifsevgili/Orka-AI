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
addCheck("Landing has real ORKA module copy", landing.includes("Öğrenci sinyali yakalandı") && landing.includes("NotebookLM") && landing.includes("QA ve sistem güveni"));
addCheck("Landing keeps P4 product depth", landing.includes("Plan") && landing.includes("Wiki") && landing.includes("Quiz") && landing.includes("Sesli Sınıf") && landing.includes("IDE"));

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
addCheck("Learning signal API exposed", api.includes("/learning/signal") && api.includes("recordSignal"));
addCheck("Learning quality API exposed", api.includes("/learning-quality/topic") && api.includes("getTopicQuality"));
addCheck("Assessment calibration API exposed", api.includes("/assessment/topic") && api.includes("runCalibration"));
addCheck("Adaptive quiz API exposed", api.includes("/quiz/adaptive/start") && api.includes("getAdaptiveNext") && api.includes("answerAdaptive"));
addCheck("Tutor timeline API exposed", api.includes("/tutor/events/session") && api.includes("getSessionTimeline"));
addCheck("Standards alignment API exposed", api.includes("/standards/topic") && api.includes("StandardsAPI"));
addCheck("Production readiness API exposed", api.includes("/production-readiness/v1") && api.includes("ProductionReadinessAPI"));
addCheck("Tool capability API exposed", api.includes("/tools/capabilities") && api.includes("ToolsAPI"));
addCheck("Learning APIs exposed", api.includes("FlashcardsAPI") && api.includes("ReviewAPI") && api.includes("DailyChallengeAPI") && api.includes("BookmarksAPI"));
addCheck("Plan diagnostic has explicit intent gate API", api.includes("analyzePlanIntent") && api.includes("/quiz/plan-diagnostic/intent"));
addCheck("Stream APIs use authenticated fetch wrapper", api.includes("export const authenticatedFetch") && api.includes("ChatAPI") && api.includes('authenticatedFetch("/api/chat/stream"') && api.includes('authenticatedFetch("/api/korteks/research-stream"') && api.includes('authenticatedFetch("/api/korteks/research-file"'));
addCheck("Stream APIs never send null bearer tokens", !api.includes("Bearer null") && !api.includes("Bearer undefined"));
addCheck("Dashboard coordination contract is typed", api.includes("coordinationScope?:") && api.includes("coordinationHealth?:") && api.includes("activeLessonTopicId"));
addCheck("Korteks sync and stream contracts are separate", api.includes("KorteksSyncResponseDto") && api.includes("researchSync") && api.includes("/api/korteks/research-stream"));
addCheck("Auth cleanup is scoped to Orka keys", api.includes("storage.clear") && !api.includes("localStorage.clear()"));

const toolContext = read("src/contexts/ToolCapabilitiesContext.tsx");
addCheck("Tool capability context drives visibility", toolContext.includes("ToolsAPI.getCapabilities") && toolContext.includes("isVisibleForUser"));

const dashboard = read("src/components/DashboardPanel.tsx");
addCheck("Learning signal book visible", dashboard.includes("Öğrenci Sinyal Defteri") && dashboard.includes("learningSignalBook"));
addCheck("Dashboard guidance and coordination visibility exist", dashboard.includes("Sıradaki en iyi adım") && dashboard.includes("Koordinasyon özeti") && dashboard.includes("coordinationHealth"));
addCheck("Learning guidance pack surfaces are visible", dashboard.includes("Çalışma kuyruğu") && dashboard.includes("Eksiklerini tamamla") && dashboard.includes("Kaldığın dersten devam et") && dashboard.includes("buildWeakConceptActionQueue"));
addCheck("Coordination health uses user-facing labels", dashboard.includes("Kaynaklar hazır") && dashboard.includes("Wiki eksik olabilir") && dashboard.includes("Quiz kanıtı zayıf") && dashboard.includes("RAG kaynak kalitesi iyi"));
addCheck("Dashboard source coverage coach is visible", dashboard.includes("Kaynak kapsaması") && dashboard.includes("Bu konuda kaynak eksik olabilir.") && dashboard.includes("Kaynak kalitesi zayıf; yeni kaynak eklemek faydalı olabilir.") && dashboard.includes("deriveSourceCoverageCoach"));

const healthHud = read("src/components/SystemHealthHUD.tsx");
addCheck("Admin HUD shows learning bridges", healthHud.includes("learningBridge") && healthHud.includes("Agent bridge monitor"));

const wiki = read("src/components/WikiMainPanel.tsx");
addCheck("Wiki actions create learning signals", wiki.includes("recordWikiAction") && wiki.includes("WikiActionClicked"));
addCheck("Wiki remains active while OrkaLM reuses notebook source surface", wiki.includes('mode?: "wiki" | "orkalm"') && wiki.includes("OrkaLM Kaynak Notebook") && wiki.includes("Notebook Kaynak"));
addCheck("Notebook tools refresh after source activity", wiki.includes("notebookRefreshTick") && wiki.includes("setNotebookRefreshTick"));
addCheck("Wiki source graph visible", wiki.includes("Kaynak graf") && wiki.includes("sourceGraph"));
addCheck("Wiki source evidence panel visible", wiki.includes("Kaynak Kan") && wiki.includes("sourceCitations") && wiki.includes("handleSourcePageNav"));
addCheck("Wiki source evidence trust strip visible", wiki.includes("source-evidence-trust-strip") && wiki.includes("Citation trail") && wiki.includes("Kaynak güveni"));
addCheck("Wiki citation chips expose scope summaries", wiki.includes("citationScopeSummary") && wiki.includes("citationDisplayTitle") && wiki.includes("citationPrimaryLabel"));
addCheck("Wiki source coverage coach is visible", wiki.includes("Kaynak kapsaması") && wiki.includes("Bu konu için kaynaklar hazır.") && wiki.includes("RAG yanıtları için yeterli kaynak bulunamayabilir") && wiki.includes("buildWikiSourceCoverageCoach"));
addCheck("Wiki learning trace summary is user-facing", wiki.includes("WikiLearningTraceSummary") && wiki.includes("Orka bu turda") && wiki.includes("Bu cevap kaynaklarla desteklendi.") && wiki.includes("Quiz/pratik kanıtı güncellendi."));
addCheck("Wiki study pack entry is visible", wiki.includes("Wiki Çalışma Paketi") && wiki.includes("Bu konuyu çalış") && wiki.includes("Özeti oku") && wiki.includes("Kavramları gözden geçir") && wiki.includes("Kartlarla çalış"));
addCheck("Wiki weak queue context is visible", wiki.includes("Bu konuda çalışma kuyruğu sinyali var.") && wiki.includes("Zayıf kavramı Wiki’den tekrar edebilir") && wiki.includes("wiki-study-reinforcement"));

const sidebar = read("src/components/LeftSidebar.tsx");
addCheck("Topic readiness badges are visible", sidebar.includes("TopicReadinessBadge") && sidebar.includes("Hazır") && sidebar.includes("Dikkat") && sidebar.includes("Yeni") && sidebar.includes("getTopicReadinessBadge"));

const citationDisplay = read("src/lib/citationDisplay.ts");
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
addCheck("Chat metadata chips render additively", chatMessage.includes("ChatMetadataChips") && chatMessage.includes("usedTools") && chatMessage.includes("fallbackReason"));
addCheck("Chat citation chips expose scope summaries", chatMessage.includes("citationScopeSummary") && chatMessage.includes("citationDisplayTitle") && chatMessage.includes("citationPrimaryLabel"));
addCheck("Chat learning trace summary is user-facing", chatMessage.includes("LearningTraceSummaryLite") && chatMessage.includes("Orka bu turda") && chatMessage.includes("Bu cevap kaynaklarla desteklendi.") && chatMessage.includes("Henüz öğrenme izi oluşmadı."));
addCheck("Live tutor trace timeline is rendered", chatMessage.includes("LiveTutorTrace") && chatMessage.includes("TutorAPI.getSessionTimeline") && chatMessage.includes("Tutor izi"));
addCheck("Plan mode requires intent confirmation before learning research", chatPanel.includes("pendingPlanIntent") && chatPanel.includes("Onayla ve araştır") && chatPanel.includes("approvedResearchIntent"));
addCheck("Plan diagnostic preserves quality metadata", api.includes("conceptGraphQualityStatus") && chatPanel.includes("qualityReportId"));
addCheck("Plan mode exposes meaningful staged UX", chatPanel.includes("Niyet ayrılıyor") && chatPanel.includes("Bağlam taranıyor") && chatPanel.includes("Seviye testi kuruluyor") && chatPanel.includes("Öğrenme yolu üretiliyor"));

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
addCheck("Quiz feedback copy is pedagogical", quiz.includes("Tekrar edilmesi iyi olur") && quiz.includes("Bu cevap doğru değil") && !quiz.includes("Harika gidiyorsun"));
addCheck("Quiz wrong answer recovery CTA is visible", quiz.includes("Toparlanma adımı") && quiz.includes("Tutor’a sor") && quiz.includes("Wiki’de tekrar et") && quiz.includes("Benzer pratik çöz"));
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
addCheck("P5 YouTube transcript plugin is in SK bridge", program.includes("YouTubeTranscriptPlugin") && program.includes("AddFromObject(sp.GetRequiredService<YouTubeTranscriptPlugin>())"));

for (const check of checks) {
  const icon = check.pass ? "OK" : "FAIL";
  console.log(`${icon} ${check.name}${check.detail ? ` - ${check.detail}` : ""}`);
}

if (failures.length > 0) {
  console.error(`\nUI smoke failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log("\nUI smoke passed.");
