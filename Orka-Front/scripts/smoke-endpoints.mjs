import fs from "node:fs";
import path from "node:path";

const frontRoot = process.cwd();
const repoRoot = path.resolve(frontRoot, "..");

const failures = [];

function read(relativePath, root = repoRoot) {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

function addCheck(name, pass, detail = "") {
  const icon = pass ? "OK" : "FAIL";
  console.log(`${icon} ${name}${detail ? ` - ${detail}` : ""}`);
  if (!pass) failures.push(`${name}${detail ? `: ${detail}` : ""}`);
}

const api = read("src/services/api.ts", frontRoot);

const frontendContracts = [
  ["/auth/login", "Auth login"],
  ["/auth/register", "Auth register"],
  ["/auth/refresh", "Auth refresh"],
  ["/user/me", "User me"],
  ["/topics", "Topics"],
  ["/chat/message", "Chat message"],
  ["/api/chat/stream", "Chat stream"],
  ["/dashboard/today", "Dashboard today"],
  ["/dashboard/stats", "Dashboard stats"],
  ["/wiki/", "Wiki routes"],
  ["/sources/upload", "Sources upload"],
  ["/sources/topic/", "Sources topic quality"],
  ["/quiz/attempt", "Quiz attempt"],
  ["/quiz/plan-diagnostic/intent", "Plan diagnostic intent gate"],
  ["/quiz/plan-diagnostic/start", "Plan diagnostic start"],
  ["/learning/signal", "Learning signal"],
  ["/learning-quality/topic/", "Learning quality report"],
  ["/rag-evaluation/run", "RAG evaluation trigger"],
  ["/tutor/state/topic/", "Tutor topic state"],
  ["/tutor/trace/", "Tutor action trace"],
  ["/tutor/pedagogy/topic/", "Tutor pedagogy topic"],
  ["/tutor/pedagogy/run/", "Tutor pedagogy run"],
  ["/tutor/pedagogy/evaluate/recent", "Tutor pedagogy evaluation trigger"],
  ["/tutor/artifacts/", "Tutor artifact"],
  ["/tutor/style-signal", "Tutor style signal"],
  ["/classroom/session", "Classroom session"],
  ["/audio/overview", "Audio overview"],
  ["/code/run", "Code run"],
  ["/korteks/research", "Korteks stream"],
  ["/tools/capabilities", "Tool capabilities"],
  ["/flashcards", "Flashcards"],
  ["/review/due", "Review due"],
  ["/daily-challenge", "Daily Challenge"],
  ["/bookmarks", "Bookmarks"],
];

for (const [needle, label] of frontendContracts) {
  addCheck(`Frontend API exposes ${label}`, api.includes(needle), needle);
}

const controllerContracts = [
  ["Orka.API/Controllers/AuthController.cs", '[Route("api/auth")]'],
  ["Orka.API/Controllers/UserController.cs", '[Route("api/user")]'],
  ["Orka.API/Controllers/TopicsController.cs", '[Route("api/topics")]'],
  ["Orka.API/Controllers/ChatController.cs", '[Route("api/chat")]'],
  ["Orka.API/Controllers/DashboardController.cs", '[Route("api/dashboard")]'],
  ["Orka.API/Controllers/WikiController.cs", '[Route("api/wiki")]'],
  ["Orka.API/Controllers/SourcesController.cs", '[Route("api/sources")]'],
  ["Orka.API/Controllers/QuizController.cs", '[Route("api/quiz")]'],
  ["Orka.API/Controllers/LearningController.cs", '[Route("api/learning")]'],
  ["Orka.API/Controllers/LearningQualityController.cs", '[Route("api/learning-quality")]'],
  ["Orka.API/Controllers/TutorController.cs", '[Route("api/tutor")]'],
  ["Orka.API/Controllers/ClassroomController.cs", '[Route("api/classroom")]'],
  ["Orka.API/Controllers/AudioController.cs", '[Route("api/audio")]'],
  ["Orka.API/Controllers/CodeController.cs", '[Route("api/code")]'],
  ["Orka.API/Controllers/KorteksController.cs", '[Route("api/korteks")]'],
  ["Orka.API/Controllers/ToolsController.cs", '[Route("api/tools")]'],
  ["Orka.API/Controllers/FlashcardsController.cs", '[Route("api/flashcards")]'],
  ["Orka.API/Controllers/ReviewController.cs", '[Route("api/review")]'],
  ["Orka.API/Controllers/DailyChallengeController.cs", '[Route("api/daily-challenge")]'],
  ["Orka.API/Controllers/BookmarksController.cs", '[Route("api/bookmarks")]'],
];

for (const [file, route] of controllerContracts) {
  const full = path.join(repoRoot, file);
  addCheck(`Backend controller exists for ${route}`, fs.existsSync(full), file);
  if (fs.existsSync(full)) {
    addCheck(`Backend route ${route}`, read(file).includes(route), file);
  }
}

const dashboard = read("Orka.API/Controllers/DashboardController.cs");
for (const route of ["/api/learning/signal", "/api/code/run", "/api/classroom/session"]) {
  addCheck(`Admin endpoint health lists ${route}`, dashboard.includes(route));
}
addCheck("Admin health exposes learning bridge payload", dashboard.includes("learningBridge") && dashboard.includes("bridgeHealth"));

if (failures.length > 0) {
  console.error(`\nEndpoint smoke failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log("\nEndpoint smoke passed.");
