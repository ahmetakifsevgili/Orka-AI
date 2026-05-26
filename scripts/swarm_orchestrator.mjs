#!/usr/bin/env node
// ============================================================================
// Orka AI — Swarm Pipeline Orchestration Engine (Multi-Agent Sync v2)
// ============================================================================
// Bu betik, Business Analyst, Architect, Developer ve QA Reviewer ajanlarının
// senkronize bir şekilde çalışmasını sağlayan ortak durum makinesini (State Machine)
// yönetir. JSON sözleşmelerini doğrular, döngü kırıcıları (Reflection Loop Breaker)
// kontrol eder ve pipeline durumunu anlık izler.
//
// Kullanım:
//   node scripts/swarm_orchestrator.mjs --action=init --feature-id=feat-auth
//   node scripts/swarm_orchestrator.mjs --action=next
//   node scripts/swarm_orchestrator.mjs --action=status
// ============================================================================

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const STATE_FILE = path.join(ROOT, "scripts", "reports", "swarm_state.json");
const ENVELOPE_DIR = path.join(ROOT, "scripts", "reports", "envelopes");

// ── Renkli Konsol Sabitleri (Zinc + Emerald + Amber) ────────────────────────
const C = {
  reset: "\x1b[0m",
  bold: "\x1b[1m",
  dim: "\x1b[2m",
  zinc: "\x1b[37m",
  emerald: "\x1b[32m",
  amber: "\x1b[33m",
  red: "\x1b[31m",
  sky: "\x1b[36m",
};

// ── CLI Argümanları ──────────────────────────────────────────────────────────
const args = Object.fromEntries(
  process.argv.slice(2).map((a) => {
    const m = a.match(/^--([^=]+)(?:=(.*))?$/);
    return m ? [m[1], m[2] ?? "true"] : [a, "true"];
  })
);

// ── Yardımcı Loglama Fonksiyonları ──────────────────────────────────────────
function log(msg, color = "") {
  console.log(color ? `${color}${msg}${C.reset}` : msg);
}

function printHeader(title) {
  log(`\n${C.bold}${C.sky}🔱 [ORKA SWARM] ${title}${C.reset}`);
  log("=".repeat(60), C.dim);
}

// ── Ajan Rol & Durum Eşleşmeleri ─────────────────────────────────────────────
const PIPELINE_STATES = {
  INIT: "CLIENT_INPUT_AWAITING",
  BA_DRAFT: "USER_STORIES_DRAFT",
  ARCHITECT_TDD: "TECH_DESIGN_PENDING",
  BA_APPROVE: "USER_STORIES_APPROVED",
  DB_LEAD_SCHEMA: "SCHEMA_DESIGN_APPROVED",
  DEVELOPER_SUBMIT: "IMPLEMENTATION_READY",
  TECH_LEAD_REVIEW: "CODE_REVIEW_PENDING",
  QA_VERIFICATION: "QA_VERIFICATION_PENDING",
  PRODUCTION_READY: "READY_FOR_PRODUCTION",
  ESCALATED: "ARCHITECT_REVIEW_REQUIRED"
};

// ── Varsayılan Boş Durum Şablonu ─────────────────────────────────────────────
const createInitialState = (featureId, type = "both") => ({
  schemaVersion: "1.0",
  pipelineId: "orka-swarm-orchestrator",
  featureId: featureId ?? `feat-${Date.now()}`,
  type, // 'frontend', 'backend', veya 'both'
  currentState: PIPELINE_STATES.INIT,
  iteration: 1,
  rejectionCount: 0,
  history: [],
  artifacts: {
    userStories: null,
    techDesign: null,
    dbSchemaOrTokens: null,
    devSubmission: null,
    reviewLog: null,
    qaReport: null
  }
});

// ── Durumu Yükle ve Kaydet ───────────────────────────────────────────────────
async function loadState() {
  try {
    const raw = await fs.readFile(STATE_FILE, "utf-8");
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

async function saveState(state) {
  await fs.mkdir(path.dirname(STATE_FILE), { recursive: true });
  await fs.mkdir(ENVELOPE_DIR, { recursive: true });
  await fs.writeFile(STATE_FILE, JSON.stringify(state, null, 2), "utf-8");
}

// ── Şema Doğrulama Motoru (Validation Gate) ──────────────────────────────────
function validateEnvelope(envelope) {
  const requiredKeys = ["schemaVersion", "pipelineId", "featureId", "fromAgent", "toAgent", "status", "iteration", "payloadType", "payload"];
  for (const key of requiredKeys) {
    if (!(key in envelope)) {
      throw new Error(`Zarf doğrulama hatası: Eksik alan '${key}'`);
    }
  }
  return true;
}

// ── Ana Orchestration Mantığı (State Machine Runner) ─────────────────────────
async function initializeSwarm(featureId, type) {
  printHeader("SWARM BAŞLATILIYOR");
  const state = createInitialState(featureId, type);
  state.currentState = PIPELINE_STATES.BA_DRAFT;
  state.history.push({
    timestamp: new Date().toISOString(),
    from: "CLIENT",
    to: "BA",
    state: state.currentState,
    message: "İş gereksinimleri alındı. BA taslak çıkarmaya başlıyor."
  });
  
  await saveState(state);
  log(`✔ Swarm başarıyla kuruldu. Özellik ID: ${C.bold}${state.featureId}${C.reset}`, C.emerald);
  log(`➜ Aktif Aşama: ${C.amber}${state.currentState}${C.reset}`);
  log(`🤖 Sıradaki Ajan: ${C.bold}Business Analyst (orka_agent_business_analyst)${C.reset}`);
}

async function advanceSwarm(envelopePath) {
  printHeader("DURUM GEÇİŞİ TETİKLENİYOR");
  const state = await loadState();
  if (!state) {
    log("❌ Aktif bir Swarm durumu bulunamadı! Lütfen önce --action=init ile başlatın.", C.red);
    return;
  }

  let envelope;
  try {
    const raw = await fs.readFile(envelopePath, "utf-8");
    envelope = JSON.parse(raw);
  } catch (err) {
    log(`❌ Zarf dosyası okunamadı: ${err.message}`, C.red);
    return;
  }

  try {
    validateEnvelope(envelope);
  } catch (err) {
    log(`❌ ${err.message}`, C.red);
    return;
  }

  log(`📥 Gelen Paket Türü: ${C.sky}${envelope.payloadType}${C.reset} | Gönderen: ${C.bold}${envelope.fromAgent}${C.reset}`);
  log(`🔄 Mevcut pipeline durumu: ${C.amber}${state.currentState}${C.reset}`);

  // Durum Makinesi Geçiş Koşulları (State Transition Logic)
  let nextState = state.currentState;
  let nextAgent = "";
  let actionDescription = "";

  switch (state.currentState) {
    case PIPELINE_STATES.BA_DRAFT:
      if (envelope.payloadType === "USER_STORIES" && envelope.status === "DRAFT") {
        nextState = PIPELINE_STATES.ARCHITECT_TDD;
        state.artifacts.userStories = envelope.payload;
        nextAgent = "Architect (orka_agent_architect)";
        actionDescription = "Taslak Kullanıcı Hikayeleri onaylandı. Architect TDD hazırlıyor.";
      }
      break;

    case PIPELINE_STATES.ARCHITECT_TDD:
      if (envelope.payloadType === "BTDD" || envelope.payloadType === "TDD") {
        if (envelope.status === "APPROVED") {
          nextState = PIPELINE_STATES.BA_APPROVE;
          state.artifacts.techDesign = envelope.payload;
          nextAgent = "Business Analyst (orka_agent_business_analyst)";
          actionDescription = "Teknik Tasarım (TDD) mimar tarafından onaylandı. BA hikayeleri kesinleştiriyor.";
        } else if (envelope.status === "CLARIFICATION_REQUIRED") {
          nextState = PIPELINE_STATES.BA_DRAFT;
          nextAgent = "Business Analyst (orka_agent_business_analyst)";
          actionDescription = "TDD çizimi için BA'den açıklama talep edildi. Durum taslağa geri çekildi.";
        }
      }
      break;

    case PIPELINE_STATES.BA_APPROVE:
      if (envelope.payloadType === "USER_STORIES" && envelope.status === "APPROVED") {
        nextState = PIPELINE_STATES.DB_LEAD_SCHEMA;
        state.artifacts.userStories = envelope.payload;
        nextAgent = "DB & Schema Lead (orka_agent_architect)";
        actionDescription = "Kullanıcı Hikayeleri onaylandı. DB Lead veritabanı şemasını / tasarım tokenlarını hazırlıyor.";
      }
      break;

    case PIPELINE_STATES.DB_LEAD_SCHEMA:
      if (envelope.payloadType === "DB_SCHEMAS" || envelope.payloadType === "DESIGN_TOKENS") {
        if (envelope.status === "APPROVED") {
          nextState = PIPELINE_STATES.DEVELOPER_SUBMIT;
          state.artifacts.dbSchemaOrTokens = envelope.payload;
          nextAgent = "Developer (orka_agent_developer)";
          actionDescription = "Şema/Tasarım Token sözleşmesi onaylandı. Kodlama süreci başlıyor.";
        } else if (envelope.status === "SCHEMA_REJECTED") {
          nextState = PIPELINE_STATES.ARCHITECT_TDD;
          nextAgent = "Architect (orka_agent_architect)";
          actionDescription = "Veritabanı modeli mimari kısıtlar nedeniyle reddedildi. Mimari revizyon gerekiyor.";
        }
      }
      break;

    case PIPELINE_STATES.DEVELOPER_SUBMIT:
      if (envelope.payloadType === "DEV_SUBMISSION") {
        if (envelope.status === "READY_FOR_REVIEW") {
          nextState = PIPELINE_STATES.TECH_LEAD_REVIEW;
          state.artifacts.devSubmission = envelope.payload;
          nextAgent = "Tech Lead / Reviewer (orka_agent_reviewer_qa)";
          actionDescription = "Geliştirici kodu tamamladı. Kod inceleme süreci (PR) başlatıldı.";
        } else if (envelope.status === "IMPLEMENTATION_BLOCKED") {
          nextState = PIPELINE_STATES.ARCHITECT_TDD;
          nextAgent = "Architect (orka_agent_architect)";
          actionDescription = "Kodlamada tıkanıklık oluştu. Teknik mimari dökümanının revize edilmesi gerekiyor.";
        }
      }
      break;

    case PIPELINE_STATES.TECH_LEAD_REVIEW:
      if (envelope.payloadType === "REVIEW_LOG") {
        state.artifacts.reviewLog = envelope.payload;
        if (envelope.status === "APPROVED") {
          nextState = PIPELINE_STATES.QA_VERIFICATION;
          nextAgent = "QA Tester (orka_agent_reviewer_qa)";
          actionDescription = "Kod incelemesi başarıyla tamamlandı. Otomatik sandbox E2E test aşamasına geçiliyor.";
        } else if (envelope.status === "REJECTED") {
          state.rejectionCount++;
          actionDescription = `Kod incelemesi reddedildi (Hata Döngüsü #${state.rejectionCount}).`;
          
          // ⚠️ Reflection Loop Breaker (3. Ret sonrası tırmandırma)
          if (state.rejectionCount >= 3) {
            nextState = PIPELINE_STATES.ESCALATED;
            nextAgent = "Architect (orka_agent_architect)";
            actionDescription += " [DÖNGÜ KIRICI TETİKLENDİ] 3. PR reddi! Durum mimara (Escalation) devredildi.";
          } else {
            nextState = PIPELINE_STATES.DEVELOPER_SUBMIT;
            nextAgent = "Developer (orka_agent_developer)";
            actionDescription += " Geliştiricinin düzeltme yapması bekleniyor.";
          }
        }
      }
      break;

    case PIPELINE_STATES.QA_VERIFICATION:
      if (envelope.payloadType === "QA_REPORT") {
        state.artifacts.qaReport = envelope.payload;
        if (envelope.status === "PASSED") {
          nextState = PIPELINE_STATES.PRODUCTION_READY;
          nextAgent = "CLIENT / CI-CD";
          actionDescription = "Tüm kabul kriterleri ve E2E sandbox kanıtları doğrulandı. Canlı yayına hazır! 🎉";
        } else if (envelope.status === "FAILED" || envelope.status === "BLOCKED_EVIDENCE_REQUIRED") {
          nextState = PIPELINE_STATES.DEVELOPER_SUBMIT;
          nextAgent = "Developer (orka_agent_developer)";
          actionDescription = `QA testlerinden geçilemedi (${envelope.status}). Hata kaydı oluşturuldu, düzeltme bekleniyor.`;
        }
      }
      break;

    case PIPELINE_STATES.ESCALATED:
      // Mimarın müdahalesi sonrasında döngü kırılır
      if (envelope.payloadType === "ESCALATION" && envelope.status === "APPROVED") {
        state.rejectionCount = 0; // Sayacı sıfırla
        nextState = PIPELINE_STATES.DEVELOPER_SUBMIT;
        nextAgent = "Developer (orka_agent_developer)";
        actionDescription = "Mimar eskalasyonu çözdü ve kapsamı güncelledi. Kodlama döngüsü sıfırlandı.";
      }
      break;

    default:
      log("⚠️ Durum geçişi tetiklenemedi veya geçersiz bir akış sağlandı.", C.amber);
      return;
  }

  // Durumu güncelle
  state.currentState = nextState;
  state.history.push({
    timestamp: new Date().toISOString(),
    from: envelope.fromAgent,
    to: envelope.toAgent,
    state: nextState,
    message: actionDescription
  });

  await saveState(state);

  // Zarfı arşivle
  const archivePath = path.join(ENVELOPE_DIR, `${envelope.payloadType}-${Date.now()}.json`);
  await fs.writeFile(archivePath, JSON.stringify(envelope, null, 2), "utf-8");

  log(`✔ Başarılı Geçiş!`, C.emerald);
  log(`➜ Yeni Durum: ${C.bold}${C.sky}${state.currentState}${C.reset}`);
  log(`🤖 Sıradaki Sorumlu: ${C.bold}${nextAgent}${C.reset}`);
  log(`📝 Olay: ${C.dim}${actionDescription}${C.reset}`);
}

async function showStatus() {
  printHeader("SWARM GÜNCEL DURUM RAPORU");
  const state = await loadState();
  if (!state) {
    log("⚠️ Aktif bir Swarm çalışması bulunmuyor. Başlatmak için: --action=init", C.amber);
    return;
  }

  log(` Özellik ID   : ${C.bold}${state.featureId}${C.reset}`);
  log(` Aktif Durum  : ${C.bold}${C.sky}${state.currentState}${C.reset}`);
  log(` İterasyon    : ${C.bold}${state.iteration}${C.reset}`);
  log(` Red Adedi    : ${state.rejectionCount >= 2 ? C.red : C.emerald}${state.rejectionCount} / 3${C.reset}`);
  log(` Pipeline Tipi: ${C.bold}${state.type.toUpperCase()}${C.reset}`);
  log("\n📌 Yapıtlar (Artifacts) Sağlık Durumu:", C.bold);
  
  for (const [key, val] of Object.entries(state.artifacts)) {
    const status = val ? `${C.emerald}✔ Mevcut${C.reset}` : `${C.red}✘ Eksik${C.reset}`;
    log(`   - ${key.padEnd(16)}: ${status}`);
  }

  log("\n📋 Son 3 Durum Geçiş Kaydı:", C.bold);
  const recent = state.history.slice(-3).reverse();
  for (const h of recent) {
    log(`   [${new Date(h.timestamp).toLocaleTimeString()}] ${C.bold}${h.from} ➜ ${h.to}${C.reset} | ${C.dim}${h.message}${C.reset}`);
  }
}

// ── CLI Kontrol Paneli ──────────────────────────────────────────────────────
async function main() {
  const action = args["action"] ?? "status";
  
  if (action === "init") {
    await initializeSwarm(args["feature-id"], args["type"] ?? "both");
  } else if (action === "next") {
    const envFile = args["envelope"];
    if (!envFile) {
      log("❌ Hata: Geçiş yapmak için bir zarf dosyası belirtmelisiniz! --envelope=dosya_yolu.json", C.red);
      process.exit(1);
    }
    await advanceSwarm(envFile);
  } else {
    await showStatus();
  }
}

main().catch((err) => {
  console.error(`${C.red}FATAL SWARM ERROR: ${err.message}${C.reset}`);
  process.exit(2);
});
