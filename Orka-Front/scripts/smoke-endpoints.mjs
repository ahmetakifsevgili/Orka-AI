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
  ["/dashboard/stats", "Dashboard stats"],
  ["/wiki/", "Wiki routes"],
  ["/sources/upload", "Sources upload"],
  ["/quiz/attempt", "Quiz attempt"],
  ["/learning/signal", "Learning signal"],
  ["/classroom/session", "Classroom session"],
  ["/audio/overview", "Audio overview"],
  ["/code/run", "Code run"],
  ["/korteks/research", "Korteks stream"],
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
  ["Orka.API/Controllers/ClassroomController.cs", '[Route("api/classroom")]'],
  ["Orka.API/Controllers/AudioController.cs", '[Route("api/audio")]'],
  ["Orka.API/Controllers/CodeController.cs", '[Route("api/code")]'],
  ["Orka.API/Controllers/KorteksController.cs", '[Route("api/korteks")]'],
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
