import { readFileSync } from "node:fs";

function read(path) {
  return readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
}

let failed = false;
function addCheck(label, condition) {
  if (!condition) {
    failed = true;
    console.error(`FAIL ${label}`);
  } else {
    console.log(`OK ${label}`);
  }
}

const navigation = read("src/lib/appNavigation.ts");
const sidebar = read("src/components/LeftSidebar.tsx");
const home = read("src/pages/Home.tsx");
const smokeUi = read("scripts/smoke-ui.mjs");
const pkg = JSON.parse(read("package.json"));

const canonicalViews = [
  "home",
  "tutor",
  "study-room",
  "review",
  "exams",
  "sources-wiki",
  "notebook",
  "code",
  "progress",
  "settings",
];

const canonicalLabels = [
  "Ana Kokpit",
  "Tutor",
  "Study Room",
  "Review / Quiz",
  "Exam War Room",
  "Sources / Wiki",
  "Notebook Studio",
  "Code IDE",
  "Progress",
  "Settings / Safety",
];

const legacyAliases = {
  dashboard: "home",
  chat: "tutor",
  classroom: "study-room",
  learning: "review",
  practice: "review",
  "central-exams": "exams",
  wiki: "sources-wiki",
  sources: "sources-wiki",
  orkalm: "notebook",
  ide: "code",
};

const canonicalPaths = [
  "/app",
  "/app/tutor",
  "/app/study-room",
  "/app/review",
  "/app/exams",
  "/app/sources",
  "/app/notebook",
  "/app/code",
  "/app/progress",
  "/app/settings",
];

addCheck("Canonical navigation contract exists", navigation.includes("CANONICAL_APP_VIEWS") && navigation.includes("APP_NAV_ITEMS") && navigation.includes("normalizeAppView"));
addCheck("Canonical views are all declared", canonicalViews.every((view) => navigation.includes(`"${view}"`)));
addCheck("Canonical labels are all declared", canonicalLabels.every((label) => navigation.includes(label)));
addCheck("Canonical deep app paths are all declared", canonicalPaths.every((appPath) => navigation.includes(`path: "${appPath}"`)) && navigation.includes("appViewPath"));
addCheck("Legacy aliases redirect to canonical modes", Object.entries(legacyAliases).every(([legacy, canonical]) => navigation.includes(`${legacy.includes("-") ? `"${legacy}"` : legacy}: "${canonical}"`)));
addCheck("Sidebar consumes the shared navigation contract", sidebar.includes("APP_NAV_ITEMS") && sidebar.includes("normalizeAppView") && !sidebar.includes("const NAV_ITEMS"));
addCheck("Sidebar keeps Settings / Safety in the app shell", sidebar.includes("Settings / Safety") && sidebar.includes('onViewChange("settings")'));
addCheck("Home uses URL-aware view normalization", home.includes("initialView") && home.includes("appViewPath(activeView)") && home.includes("normalizeAppView(saved)") && !home.includes("const VALID_VIEWS"));
addCheck("Logout returns to canonical Home", home.includes('setActiveView("home")') && !home.includes('setActiveView("dashboard")'));
addCheck("UI smoke verifies canonical labels", canonicalLabels.every((label) => smokeUi.includes(label)));
addCheck("Quick smoke includes navigation contract", pkg.scripts["quick:smoke"]?.includes("smoke:navigation"));

if (failed) {
  process.exit(1);
}

console.log("\nNavigation smoke passed.");
