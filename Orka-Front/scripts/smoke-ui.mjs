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
    else if (/\.(tsx?|css)$/.test(entry.name)) output.push(full);
  }
  return output;
}

const mojibake = /Ãƒ|Ã„|Ã…|Ã¢â‚¬|Ã¢â€ |Ã¢Å“|ÄŸÅ¸|ï¿½/;
const dirty = walk(srcRoot).filter((file) => mojibake.test(fs.readFileSync(file, "utf8")));
addCheck("Turkish mojibake guard", dirty.length === 0, dirty.map((file) => path.relative(root, file)).join(", "));

const landing = read("src/pages/Landing.tsx");
addCheck("Landing has real ORKA module copy", landing.includes("Öğrenci sinyali yakalandı") && landing.includes("NotebookLM") && landing.includes("QA ve sistem güveni"));
addCheck("Landing keeps P4 product depth", landing.includes("Plan") && landing.includes("Wiki") && landing.includes("Quiz") && landing.includes("Sesli Sınıf") && landing.includes("IDE"));

const css = read("src/index.css");
addCheck("Mist Comfort utilities exist", css.includes(".orka-surface") && css.includes(".orka-panel") && css.includes(".orka-focus"));

const viteConfig = fs.readFileSync(path.join(root, "vite.config.ts"), "utf8");
addCheck("Vite production build warnings are bounded", viteConfig.includes("chunkSizeWarningLimit") && !viteConfig.includes("manualChunks"));

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

const dashboard = read("src/components/DashboardPanel.tsx");
addCheck("Learning signal book visible", dashboard.includes("Öğrenci Sinyal Defteri") && dashboard.includes("learningSignalBook"));

const healthHud = read("src/components/SystemHealthHUD.tsx");
addCheck("Admin HUD shows learning bridges", healthHud.includes("learningBridge") && healthHud.includes("Agent bridge monitor"));

const wiki = read("src/components/WikiMainPanel.tsx");
addCheck("Wiki actions create learning signals", wiki.includes("recordWikiAction") && wiki.includes("WikiActionClicked"));
addCheck("Notebook tools refresh after source activity", wiki.includes("notebookRefreshTick") && wiki.includes("setNotebookRefreshTick"));
addCheck("Wiki source graph visible", wiki.includes("Kaynak graf") && wiki.includes("sourceGraph"));
addCheck("Wiki source evidence panel visible", wiki.includes("Kaynak Kan") && wiki.includes("sourceCitations") && wiki.includes("handleSourcePageNav"));

const richMarkdown = read("src/components/RichMarkdown.tsx");
addCheck("Citation clicks are observable", richMarkdown.includes("onCitationClick") && richMarkdown.includes("citationKind") === false);
addCheck("Mermaid stays lazy loaded", richMarkdown.includes('import("mermaid")') && !richMarkdown.includes('import mermaid from "mermaid"'));

const home = read("src/pages/Home.tsx");
addCheck("Toast import has no static/dynamic conflict", !home.includes('import("react-hot-toast")'));

const packageJson = read("package.json");
const onboarding = read("src/components/PremiumOnboardingTour.tsx");
addCheck("P5 premium onboarding is wired", packageJson.includes('"driver.js"') && home.includes("usePremiumOnboarding") && onboarding.includes("tour-new-topic") && onboarding.includes("tour-nav-dashboard") && onboarding.includes("tour-nav-wiki") && onboarding.includes("tour-nav-ide"));

const classroomPlayer = read("src/components/ClassroomAudioPlayer.tsx");
addCheck("Classroom active segment bridge", classroomPlayer.includes("activeSegment") && classroomPlayer.includes("Anlamadım"));
addCheck("Classroom ask answer joins audio queue", classroomPlayer.includes("queuedLines") && classroomPlayer.includes("speakLine(startIndex"));
addCheck("P5 classroom backend audio fallback", api.includes("interactionId") && api.includes("getInteractionAudio") && classroomPlayer.includes("tryPlayBackendAudio") && classroomPlayer.includes("speechSynthesis"));

const quiz = read("src/components/QuizCard.tsx");
addCheck("Quiz raw JSON leak guard", !quiz.includes("JSON.stringify(quiz") && !quiz.includes("{JSON.stringify"));
addCheck("Quiz feedback copy uses readable Turkish", quiz.includes("Quiz Cevabım") && quiz.includes("Doğru") && quiz.includes("Yanlış") && !quiz.includes("Dogru") && !quiz.includes("Yanlis"));

const tutorAgent = readRepo("Orka.Infrastructure/Services/TutorAgent.cs");
const deepPlanAgent = readRepo("Orka.Infrastructure/Services/DeepPlanAgent.cs");
const planQualityTests = readRepo("Orka.API.Tests/PlanQualityGuardTests.cs");
const program = readRepo("Orka.API/Program.cs");
addCheck("P4 visual learning validator exists", tutorAgent.includes("[P4 GÖRSEL ÖĞRENME VALIDATOR]") && tutorAgent.includes("Mermaid") && tutorAgent.includes("mikro kontrol sorusu"));
addCheck("P4 domain plan templates exist", deepPlanAgent.includes("PlanDomain.Exam") && deepPlanAgent.includes("PlanDomain.Algorithm") && deepPlanAgent.includes("PlanDomain.Math") && deepPlanAgent.includes("PlanDomain.Language"));
addCheck("P4 language learning plan template exists", deepPlanAgent.includes("[DOMAIN SABLONU - DIL OGRENIMI]") && deepPlanAgent.includes("Spaced Repetition") && deepPlanAgent.includes("Speaking Prompt"));
addCheck("P4 plan quality backend guard exists", planQualityTests.includes("PlanQualityGuardTests") && planQualityTests.includes("DeepPlan_FallbackModulesAreNotGeneric"));
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
