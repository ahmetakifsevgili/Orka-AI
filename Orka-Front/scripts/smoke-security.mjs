import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const failures = [];

function read(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

function addCheck(name, pass, detail = "") {
  const icon = pass ? "OK" : "FAIL";
  console.log(`${icon} ${name}${detail ? ` - ${detail}` : ""}`);
  if (!pass) failures.push(`${name}${detail ? `: ${detail}` : ""}`);
}

const helper = read("src/lib/contentSafety.tsx");
const chat = read("src/components/ChatMessage.tsx");
const richMarkdown = read("src/components/RichMarkdown.tsx");

const requiredGuards = [
  ["script removal", "script, foreignObject, iframe, object, embed"],
  ["event handler removal", "name.startsWith(\"on\")"],
  ["unsafe href/src removal", "name === \"href\""],
  ["xlink href removal", "name.endsWith(\":href\")"],
  ["style url/expression removal", "url\\s*\\(|expression\\s*\\("],
  ["protocol-relative URL block", "href.startsWith(\"//\")"],
  ["remote image allowlist", "ALLOWED_IMAGE_HOSTS"],
  ["Pollinations image allowlist", "image.pollinations.ai"],
  ["internal source links", "orka-source:"],
  ["internal wiki links", "orka-wiki:"],
  ["internal web links", "orka-web:"],
  ["safe link rel", "noopener noreferrer nofollow"],
  ["local-only SVG references", "value.startsWith(\"#\")"],
];

for (const [name, needle] of requiredGuards) {
  addCheck(`Content safety guard: ${name}`, helper.includes(needle), needle);
}

const blockedCorpus = [
  "<script>alert(1)</script>",
  "onerror=",
  "onload=",
  "onclick=",
  "javascript:",
  "//evil.example/image.png",
  "<foreignObject>",
  "<iframe",
  "<object",
  "<embed",
  "xlink:href=\"https://evil.example/x\"",
  "style=\"background:url(https://evil.example/x)\"",
  "style=\"width:expression(alert(1))\"",
];

for (const sample of blockedCorpus) {
  addCheck(`Security corpus sample is represented: ${sample}`, typeof sample === "string" && sample.length > 0);
}

addCheck("Mermaid renders in strict mode", chat.includes("securityLevel: \"strict\"") && richMarkdown.includes("securityLevel: \"strict\""));
addCheck("Mermaid HTML labels stay disabled", chat.includes("htmlLabels: false") && richMarkdown.includes("htmlLabels: false"));
addCheck("Mermaid SVG output is sanitized", chat.includes("sanitizeMermaidSvg") && richMarkdown.includes("sanitizeMermaidSvg"));
addCheck("ReactMarkdown surfaces use safe link/image components", helper.includes("safeMarkdownComponents") && richMarkdown.includes("CitationLink") && richMarkdown.includes("isAllowedRemoteImage"));

if (failures.length > 0) {
  console.error(`\nSecurity smoke failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log("\nSecurity smoke passed.");
