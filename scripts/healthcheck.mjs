#!/usr/bin/env node
// ================================================================
// Orka AI — Kapsamlı Sistem Sağlık Denetim Scripti
// ================================================================
// Tek komutla tüm altyapıyı tarar: infra, auth, CRUD, AI akışları,
// LLMOps telemetrisi, admin policy.  PASS/FAIL/BONUS raporu üretir.
//
// Kullanım:
//   node scripts/healthcheck.mjs
//   node scripts/healthcheck.mjs --base-url=http://stage:5065
//   node scripts/healthcheck.mjs --quick        # LLM çağrısı yok
//   node scripts/healthcheck.mjs --admin-email=me@example.com
//   node scripts/healthcheck.mjs --admin-password=...
//
// Çıktı:
//   scripts/reports/healthcheck-YYYYMMDD-HHMM.json
//   scripts/reports/healthcheck-YYYYMMDD-HHMM.md
//   Konsolda renkli PASS/FAIL tablosu + puan.
// ================================================================

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const REPORT_DIR = path.join(ROOT, "scripts", "reports");

// ── CLI Args ────────────────────────────────────────────────────
const args = Object.fromEntries(
  process.argv.slice(2).map((a) => {
    const m = a.match(/^--([^=]+)(?:=(.*))?$/);
    return m ? [m[1], m[2] ?? "true"] : [a, "true"];
  })
);

const BASE_URL = (args["base-url"] ?? "http://localhost:5065").replace(/\/$/, "");
const QUICK = args["quick"] === "true";
const ADMIN_EMAIL = args["admin-email"] ?? null;
const ADMIN_PASSWORD = args["admin-password"] ?? null;
const SCORE = { required: 0, requiredMax: 0, bonus: 0, bonusMax: 0 };
const RESULTS = [];

// ── Renkli konsol ───────────────────────────────────────────────
const C = {
  reset: "\x1b[0m",  bold: "\x1b[1m",   dim: "\x1b[2m",
  gray: "\x1b[90m",  red: "\x1b[31m",   green: "\x1b[32m",
  amber: "\x1b[33m", blue: "\x1b[36m",  magenta: "\x1b[35m",
};

function log(line, color = "") { console.log(color ? `${color}${line}${C.reset}` : line); }
function head(title) { console.log(`\n${C.bold}${C.blue}▶ ${title}${C.reset}`); }
function pass(label, pts)  { console.log(`  ${C.green}✔${C.reset} ${label} ${C.dim}(+${pts})${C.reset}`); }
function fail(label, msg)  { console.log(`  ${C.red}✘${C.reset} ${label}  ${C.red}${msg}${C.reset}`); }
function skip(label)       { console.log(`  ${C.gray}○ ${label} — atlandı${C.reset}`); }
function bonus(label, pts) { console.log(`  ${C.magenta}★${C.reset} ${label} ${C.dim}(+${pts} bonus)${C.reset}`); }

// ── Skor kaydı ──────────────────────────────────────────────────
function record(category, label, status, pts, details) {
  if (status === "PASS")  { SCORE.required += pts; SCORE.requiredMax += pts; pass(label, pts); }
  if (status === "FAIL")  { SCORE.requiredMax += pts;                         fail(label, details ?? ""); }
  if (status === "BONUS") { SCORE.bonus   += pts; SCORE.bonusMax    += pts; bonus(label, pts); }
  if (status === "MISS")  { SCORE.bonusMax += pts;                            skip(label); }
  RESULTS.push({ category, label, status, pts, details });
}

// ── HTTP yardımcıları ────────────────────────────────────────────
async function http(method, url, { token, body, raw = false, timeoutMs = 30000 } = {}) {
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), timeoutMs);
  try {
    const res = await fetch(`${BASE_URL}${url}`, {
      method,
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: ctrl.signal,
    });
    const text = await res.text();
    const json = (() => { try { return JSON.parse(text); } catch { return null; } })();
    return { ok: res.ok, status: res.status, headers: res.headers, text, json, raw };
  } catch (err) {
    return { ok: false, status: 0, error: String(err) };
  } finally {
    clearTimeout(t);
  }
}

// ── 1. Infra Testleri ───────────────────────────────────────────
async function testInfra() {
  head("1) Altyapı");

  const live = await http("GET", "/health/live");
  if (live.ok)  record("Infra", "Backend /health/live yanıt verdi", "PASS", 10);
  else          record("Infra", "Backend /health/live yanıt vermedi", "FAIL", 10, `status=${live.status} ${live.error ?? ""}`);

  const ready = await http("GET", "/health/ready");
  if (ready.ok) record("Infra", "Backend /health/ready hazır", "PASS", 10);
  else          record("Infra", "Backend /health/ready hazır değil", "FAIL", 10, `status=${ready.status}`);

  // Root health (tüm DB/Redis check)
  const root = await http("GET", "/health");
  if (root.ok)  record("Infra", "/health toplu kontrol OK", "PASS", 10);
  else          record("Infra", "/health toplu kontrol FAIL", "FAIL", 10, `status=${root.status}`);
}

// ── 2. Auth Akışı ──────────────────────────────────────────────
async function testAuth() {
  head("2) Kimlik Doğrulama");

  const stamp = Date.now();
  const email = `orka_test_${stamp}@orka.ai`;
  const password = "OrkaTester123!";

  // Register
  const reg = await http("POST", "/api/Auth/register", {
    body: { firstName: "Orka", lastName: "Tester", email, password },
  });
  if (!reg.ok) {
    record("Auth", "Register başarılı", "FAIL", 15, `status=${reg.status} body=${reg.text?.slice(0, 100)}`);
    return null;
  }
  record("Auth", "Register başarılı", "PASS", 15);

  // Login
  const login = await http("POST", "/api/Auth/login", { body: { email, password } });
  if (!login.ok || !login.json?.token) {
    record("Auth", "Login başarılı", "FAIL", 15, `status=${login.status}`);
    return null;
  }
  record("Auth", "Login başarılı", "PASS", 15);

  const token = login.json.token;
  const refreshToken = login.json.refreshToken;
  const userId = login.json.user?.id;
  const isAdmin = login.json.user?.isAdmin;

  // UserDto.IsAdmin alanı response'ta var mı?
  if (isAdmin !== undefined) record("Auth", "UserDto.isAdmin alanı mevcut", "PASS", 5);
  else                        record("Auth", "UserDto.isAdmin alanı eksik", "FAIL", 5);

  // Refresh
  const refresh = await http("POST", "/api/Auth/refresh", { body: { refreshToken } });
  if (refresh.ok && refresh.json?.token) record("Auth", "Refresh flow çalışıyor", "PASS", 10);
  else                                    record("Auth", "Refresh flow bozuk", "FAIL", 10, `status=${refresh.status}`);

  return { token, userId, email, password };
}

// ── 3. CRUD Testleri ───────────────────────────────────────────
async function testCRUD(session) {
  head("3) Core CRUD");
  if (!session) { skip("Core CRUD atlandı (auth yok)"); return null; }

  // Topic oluştur
  const topic = await http("POST", "/api/topics", {
    token: session.token,
    body: { title: `Sağlık Testi ${Date.now()}`, emoji: "🧪", category: "Test" },
  });
  if (!topic.ok || !topic.json?.id) {
    record("CRUD", "Topic oluşturma", "FAIL", 10, `status=${topic.status}`);
    return null;
  }
  record("CRUD", "Topic oluşturma", "PASS", 10);

  // Topic listele
  const list = await http("GET", "/api/topics", { token: session.token });
  if (list.ok && Array.isArray(list.json)) record("CRUD", "Topic listeleme", "PASS", 5);
  else                                       record("CRUD", "Topic listeleme", "FAIL", 5, `status=${list.status}`);

  // Dashboard stats
  const stats = await http("GET", "/api/dashboard/stats", { token: session.token });
  if (stats.ok) record("CRUD", "Dashboard stats", "PASS", 5);
  else          record("CRUD", "Dashboard stats", "FAIL", 5, `status=${stats.status}`);

  return { ...session, topicId: topic.json.id };
}

// ── 4. AI Smoke Testleri ───────────────────────────────────────
async function testAI(session) {
  head("4) AI Akışları" + (QUICK ? " (atlandı: --quick)" : ""));
  if (!session)              { skip("AI akışı atlandı (auth yok)"); return; }
  if (QUICK)                 { skip("AI akışı atlandı (--quick)");  return; }

  // GET /api/chat/test-ai — en kısa AI çağrısı
  const t0 = Date.now();
  const testAi = await http("GET", "/api/chat/test-ai", { token: session.token, timeoutMs: 90000 });
  const lat = Date.now() - t0;
  if (testAi.ok && testAi.json?.status === "Complete") {
    record("AI", `Primary AI çağrısı (${lat}ms)`, "PASS", 15);
  } else {
    record("AI", "Primary AI çağrısı", "FAIL", 15, `status=${testAi.status}`);
  }

  // Basit bir mesaj gönder
  const msg = await http("POST", "/api/chat/message", {
    token: session.token,
    body: {
      content: "Merhaba, bu sağlık testi mesajıdır. Kısa cevap ver.",
      topicId: session.topicId,
      isPlanMode: false,
    },
    timeoutMs: 120000,
  });
  if (msg.ok && msg.json?.content) {
    record("AI", "Non-stream mesaj gönderimi", "PASS", 15);

    // LLMOps sub-checks
    if (msg.json.sessionId) record("AI", "Response.sessionId dolu", "PASS", 5);
    else                     record("AI", "Response.sessionId boş", "FAIL", 5);
  } else {
    record("AI", "Non-stream mesaj gönderimi", "FAIL", 15, `status=${msg.status}`);
  }
}

// ── 5. LLMOps Telemetri ────────────────────────────────────────
async function testLLMOps(session, adminToken) {
  head("5) LLMOps Telemetri (admin gerekir)");

  if (!adminToken) {
    record("LLMOps", "Admin JWT yok — LLMOps endpoint'leri atlandı", "MISS", 25);
    return;
  }

  const health = await http("GET", "/api/dashboard/system-health", { token: adminToken });
  if (!health.ok) {
    record("LLMOps", "system-health endpoint (admin)", "FAIL", 25, `status=${health.status}`);
    return;
  }
  record("LLMOps", "system-health endpoint (admin)", "PASS", 5);

  const data = health.json;
  if (typeof data?.tokens?.total === "number")    record("LLMOps", "Token & cost izleniyor",   "PASS", 5);
  else                                              record("LLMOps", "Token & cost eksik",       "FAIL", 5);

  if (Array.isArray(data?.agents))                 record("LLMOps", "Agent metric listesi var",  "PASS", 5);
  else                                              record("LLMOps", "Agent metric listesi yok", "FAIL", 5);

  if (Array.isArray(data?.modelMix))               record("LLMOps", "Provider mix verisi var",   "BONUS", 5);
  else                                              record("LLMOps", "Provider mix eksik",       "MISS", 5);

  if (data?.llmops?.avgEvaluatorScore !== undefined) record("LLMOps", "Evaluator ortalaması hesaplanıyor", "PASS", 5);
  else                                                record("LLMOps", "Evaluator ortalaması yok",         "FAIL", 5);

  // Failover sağlığı
  const github = data?.modelMix?.find((m) => m.provider === "GitHub");
  if (github && github.percentage >= 85) bonus("Primary GitHub ≥ 85% (sağlıklı failover)", 5), SCORE.bonus += 5, SCORE.bonusMax += 5,
    RESULTS.push({ category: "LLMOps", label: "Primary ≥ 85%", status: "BONUS", pts: 5 });
  else if (github) { SCORE.bonusMax += 5; RESULTS.push({ category: "LLMOps", label: `Primary %${github.percentage?.toFixed(1)} (hedef: ≥85)`, status: "MISS", pts: 5 }); skip(`Primary %${github.percentage?.toFixed(1)}`); }
  else { SCORE.bonusMax += 5; RESULTS.push({ category: "LLMOps", label: "Primary verisi yok", status: "MISS", pts: 5 }); skip("Primary verisi yok"); }
}

// ── 6. Admin Policy ────────────────────────────────────────────
async function testAdminPolicy(nonAdminToken, adminToken) {
  head("6) Admin Policy Gating");

  const withoutAdmin = await http("GET", "/api/dashboard/system-health", { token: nonAdminToken });
  if (withoutAdmin.status === 403) {
    record("Admin", "Non-admin 403 görüyor", "PASS", 15);
  } else if (withoutAdmin.status === 401) {
    record("Admin", "Non-admin 401 (token geçersiz?)", "FAIL", 15);
  } else if (withoutAdmin.ok) {
    record("Admin", "Non-admin system-health'e girdi (POLICY BOZUK)", "FAIL", 15, "Admin gating çalışmıyor!");
  } else {
    record("Admin", `Non-admin status=${withoutAdmin.status}`, "FAIL", 15);
  }

  if (adminToken) {
    const withAdmin = await http("GET", "/api/dashboard/system-health", { token: adminToken });
    if (withAdmin.ok) record("Admin", "Admin system-health'e girdi", "PASS", 10);
    else              record("Admin", `Admin girişi reddedildi (status=${withAdmin.status})`, "FAIL", 10);
  } else {
    record("Admin", "Admin token yok — admin girişi atlandı", "MISS", 10);
  }
}

// ── Admin login (opsiyonel) ─────────────────────────────────────
async function loginAdmin() {
  if (!ADMIN_EMAIL || !ADMIN_PASSWORD) return null;
  const r = await http("POST", "/api/Auth/login", {
    body: { email: ADMIN_EMAIL, password: ADMIN_PASSWORD },
  });
  if (r.ok && r.json?.user?.isAdmin) {
    log(`${C.green}ℹ${C.reset}  Admin olarak giriş yapıldı: ${ADMIN_EMAIL}`, C.dim);
    return r.json.token;
  }
  log(`${C.amber}⚠${C.reset}  Admin login başarısız veya hesap admin değil: ${ADMIN_EMAIL}`, C.dim);
  return null;
}

// ── Rapor üretimi ───────────────────────────────────────────────
async function writeReports(startedAt, durationMs) {
  await fs.mkdir(REPORT_DIR, { recursive: true });
  const ts = new Date(startedAt).toISOString().replace(/[:.]/g, "-").slice(0, 16);
  const base = path.join(REPORT_DIR, `healthcheck-${ts}`);

  const pctReq   = SCORE.requiredMax ? Math.round((SCORE.required / SCORE.requiredMax) * 100) : 0;
  const pctBonus = SCORE.bonusMax    ? Math.round((SCORE.bonus    / SCORE.bonusMax)    * 100) : 0;

  // Grade
  const grade = pctReq >= 95 ? "A" : pctReq >= 85 ? "B" : pctReq >= 70 ? "C" : pctReq >= 50 ? "D" : "F";

  const json = {
    startedAt: new Date(startedAt).toISOString(),
    durationMs,
    baseUrl: BASE_URL,
    quick: QUICK,
    score: { ...SCORE, percentRequired: pctReq, percentBonus: pctBonus, grade },
    results: RESULTS,
  };

  await fs.writeFile(`${base}.json`, JSON.stringify(json, null, 2), "utf-8");

  // Markdown rapor
  const failed = RESULTS.filter((r) => r.status === "FAIL");
  const passed = RESULTS.filter((r) => r.status === "PASS").length;
  const bonusItems = RESULTS.filter((r) => r.status === "BONUS");

  const md = `# Orka AI — Sağlık Denetim Raporu

**Tarih:** ${new Date(startedAt).toLocaleString("tr-TR")}
**Süre:** ${(durationMs / 1000).toFixed(1)} sn
**Base URL:** ${BASE_URL}
**Mod:** ${QUICK ? "Hızlı (LLM çağrısı yok)" : "Tam"}

## Puan

- **Zorunlu:** ${SCORE.required} / ${SCORE.requiredMax}  (%${pctReq})  —  **${grade}**
- **Bonus:**   ${SCORE.bonus} / ${SCORE.bonusMax}  (%${pctBonus})
- **Pass / Fail:** ${passed} / ${failed.length}

## Hatalar

${failed.length === 0 ? "_Hata yok. 🎉_" : failed.map((r) => `- **${r.category} — ${r.label}**  _${r.details ?? ""}_`).join("\n")}

## Bonus Elde Edilenler

${bonusItems.length === 0 ? "_Bonus yok._" : bonusItems.map((r) => `- ${r.category} — ${r.label}`).join("\n")}

## Tüm Sonuçlar

| Kategori | Test | Durum | Puan |
|---|---|---|---|
${RESULTS.map((r) => `| ${r.category} | ${r.label} | ${r.status} | ${r.pts} |`).join("\n")}
`;
  await fs.writeFile(`${base}.md`, md, "utf-8");

  return { json: `${base}.json`, md: `${base}.md`, pctReq, pctBonus, grade, failed: failed.length };
}

// ── Ana akış ────────────────────────────────────────────────────
async function main() {
  const startedAt = Date.now();

  log(`${C.bold}Orka AI Sağlık Denetimi${C.reset}`);
  log(`${C.dim}Base URL: ${BASE_URL}  |  Mod: ${QUICK ? "Hızlı" : "Tam"}${C.reset}`);

  await testInfra();
  const session = await testAuth();
  const sessionWithTopic = await testCRUD(session);
  await testAI(sessionWithTopic ?? session);
  const adminToken = await loginAdmin();
  await testLLMOps(sessionWithTopic ?? session, adminToken);
  await testAdminPolicy(session?.token ?? null, adminToken);

  const duration = Date.now() - startedAt;
  const report = await writeReports(startedAt, duration);

  // Final özet
  console.log("\n" + "─".repeat(60));
  log(`${C.bold}SONUÇ${C.reset}`);
  log(`  Zorunlu: ${SCORE.required}/${SCORE.requiredMax}  (%${report.pctReq})  —  Harf: ${C.bold}${report.grade}${C.reset}`, report.pctReq >= 85 ? C.green : report.pctReq >= 50 ? C.amber : C.red);
  log(`  Bonus:   ${SCORE.bonus}/${SCORE.bonusMax}  (%${report.pctBonus})`, C.magenta);
  log(`  Hata:    ${report.failed}`, report.failed === 0 ? C.green : C.red);
  log(`\n  JSON: ${path.relative(ROOT, report.json)}`, C.dim);
  log(`  MD:   ${path.relative(ROOT, report.md)}`, C.dim);

  process.exit(report.failed === 0 ? 0 : 1);
}

main().catch((err) => {
  console.error(`${C.red}FATAL: ${err.message}${C.reset}`);
  console.error(err.stack);
  process.exit(2);
});
