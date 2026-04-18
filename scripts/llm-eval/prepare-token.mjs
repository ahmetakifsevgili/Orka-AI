#!/usr/bin/env node
// ================================================================
// Orka LLM Eval — Test kullanıcısı için JWT hazırlayıcı.
// Kullanım:
//   node prepare-token.mjs                 (default localhost:5065)
//   node prepare-token.mjs --base=http://stage:5065
// Çıktı: ORKA_TOKEN=... satırını stdout'a basar, .env.local'a yazar.
// ================================================================

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const args = Object.fromEntries(
  process.argv.slice(2).map(a => a.replace(/^--/, "").split("="))
);
const BASE_URL = args.base || process.env.ORKA_BASE_URL || "http://localhost:5065";
const EMAIL    = "orka_eval_runner@orka.ai";
const PASSWORD = "OrkaEval!2026";

async function post(path, body) {
  const res = await fetch(`${BASE_URL}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  let data = {};
  try { data = JSON.parse(text); } catch {}
  return { status: res.status, data };
}

async function main() {
  console.log(`[eval-prep] Base: ${BASE_URL}`);

  // 1) Register (409 → zaten var, OK sayılır)
  let reg = await post("/api/auth/register", {
    email: EMAIL, password: PASSWORD, firstName: "Eval", lastName: "Runner"
  });
  if (reg.status !== 200 && reg.status !== 201 && reg.status !== 409) {
    console.error("[eval-prep] Register beklenmeyen status:", reg.status, reg.data);
    process.exit(1);
  }

  // 2) Login
  const login = await post("/api/auth/login", { email: EMAIL, password: PASSWORD });
  if (login.status !== 200) {
    console.error("[eval-prep] Login başarısız:", login.status, login.data);
    process.exit(1);
  }
  const token = login.data.accessToken || login.data.token;
  if (!token) {
    console.error("[eval-prep] accessToken yok:", login.data);
    process.exit(1);
  }

  const envPath = path.join(__dirname, ".env.local");
  const content = `ORKA_BASE_URL=${BASE_URL}\nORKA_TOKEN=${token}\n`;
  await fs.writeFile(envPath, content, "utf8");

  console.log(`[eval-prep] OK — JWT yazıldı: ${envPath}`);
  console.log(`[eval-prep] Kullanım: npx promptfoo eval --env-file .env.local`);
}

main().catch(err => { console.error(err); process.exit(1); });
