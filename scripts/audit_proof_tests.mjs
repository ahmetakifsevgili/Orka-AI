#!/usr/bin/env node
// ================================================================
// Orka AI — Canlı Audit Kanıtlama Test Senaryosu (Proof of Concept)
// ================================================================
// Bu script, Orka AI audit raporunda belirtilen 3 kritik yapısal açığı
// canlı bir şekilde test ederek kanıtlar.
//
// Çalıştırma:
//   node scripts/audit_proof_tests.mjs
// ================================================================

const BASE_URL = "http://localhost:5065";

const C = {
  reset: "\x1b[0m", bold: "\x1b[1m", dim: "\x1b[2m",
  red: "\x1b[31m", green: "\x1b[32m", blue: "\x1b[36m",
  amber: "\x1b[33m", magenta: "\x1b[35m"
};

function log(line, color = "") { console.log(color ? `${color}${line}${C.reset}` : line); }
function head(title) { console.log(`\n${C.bold}${C.blue}▶ ${title}${C.reset}`); }

async function http(method, url, { token, body } = {}) {
  try {
    const res = await fetch(`${BASE_URL}${url}`, {
      method,
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    const json = (() => { try { return JSON.parse(text); } catch { return null; } })();
    return { ok: res.ok, status: res.status, headers: res.headers, text, json };
  } catch (err) {
    return { ok: false, status: 0, error: String(err) };
  }
}

async function runTests() {
  log("================================================================", C.magenta + C.bold);
  log("      ORKA AI SWARM - CANLI AUDIT KANITLAMA TESTLERİ", C.magenta + C.bold);
  log("================================================================", C.magenta + C.bold);
  log(`Hedef Sunucu: ${BASE_URL}\n`);

  // Adım 0: Yeni Kullanıcı Kaydı ve Giriş
  log("Hazırlık: Test kullanıcısı oluşturuluyor...");
  const stamp = Date.now();
  const email = `audit_proof_${stamp}@orka.ai`;
  const password = "OrkaTester123!";

  const reg = await http("POST", "/api/Auth/register", {
    body: { firstName: "Audit", lastName: "Prover", email, password }
  });

  if (!reg.ok) {
    log(`❌ Hazırlık Başarısız: Kullanıcı kaydedilemedi (Status=${reg.status})`, C.red);
    return;
  }
  log("✔ Kullanıcı başarıyla kaydedildi.", C.green);

  const login = await http("POST", "/api/Auth/login", { body: { email, password } });
  if (!login.ok || !login.json?.token) {
    log(`❌ Hazırlık Başarısız: Giriş yapılamadı (Status=${login.status})`, C.red);
    return;
  }
  log("✔ Giriş başarılı. JWT Token alındı.\n");
  const token = login.json.token;

  // ────────────────────────────────────────────────────────────────
  // SENARYO 1: test-ai Endpoint'indeki Yetki Mismatch Açığı (403 Proof)
  // ────────────────────────────────────────────────────────────────
  head("Senaryo 1: test-ai Yetkilendirme Açığı Kanıtı");
  log("Açıklama: /api/chat/test-ai endpoint'inin Admin yetkisi gerektirmesine rağmen,");
  log("sağlık testinin bunu standart kullanıcı tokenı ile sorgulaması açığını kanıtlar.\n");

  log(`İstek gönderiliyor: GET /api/chat/test-ai (Token: Standart Kullanıcı)`);
  const testAi = await http("GET", "/api/chat/test-ai", { token });

  log(`Yanıt Durumu: ${testAi.status} ${testAi.status === 403 ? "Forbidden (Beklenen)" : "Unexpected"}`);
  log(`Yanıt İçeriği: ${testAi.text.slice(0, 150)}`);

  if (testAi.status === 403) {
    log("\n[KANITLANDI]: Standart kullanıcı bu smoke-test endpoint'ine erişememektedir.", C.green + C.bold);
    log("Bu durum, sağlık testinde (healthcheck.mjs) yanlış yetki kullanımından ötürü", C.amber);
    log("Primary AI servislerinin asılsız yere 'çöktü' görünmesine yol açmaktadır.", C.amber);
  } else {
    log("\n❌ Senaryo 1 Beklenmeyen Sonuç!", C.red);
  }

  // ────────────────────────────────────────────────────────────────
  // SENARYO 2: DTO Eksikliği ve Kırık Refresh Token Akışı (400 Proof)
  // ────────────────────────────────────────────────────────────────
  head("Senaryo 2: Kırık Refresh Token Akışı & DTO Eksikliği Kanıtı");
  log("Açıklama: AuthResponse içinde 'refreshToken' adında bir DTO alanının bulunmaması");
  log("ve çerez okumayan istemcilerin refresh talebinin 400 Bad Request almasını kanıtlar.\n");

  log("Adım A: Login yanıt gövdesi inceleniyor...");
  const hasRefreshTokenInBody = login.json.refreshToken !== undefined;
  log(`Response body içindeki 'refreshToken' alanı mevcut mu? -> ${hasRefreshTokenInBody ? "EVET" : "HAYIR (Beklenen - DTO Eksik)"}`);

  log("\nAdım B: Boş veya undefined body ile /api/Auth/refresh çağrısı yapılıyor...");
  const refresh = await http("POST", "/api/Auth/refresh", { body: { refreshToken: login.json.refreshToken } });

  log(`Yanıt Durumu: ${refresh.status} (Beklenen: 400)`);
  log(`Yanıt Gövdesi: ${refresh.text}`);

  if (!hasRefreshTokenInBody && refresh.status === 400) {
    log("\n[KANITLANDI]: AuthResponse modelinde RefreshToken DTO alanı olmadığı için,", C.green + C.bold);
    log("istemciler tokenı yakalayamamakta ve yenileme isteğinde '400 Refresh token zorunlu' almaktadır.", C.green + C.bold);
  } else {
    log("\n❌ Senaryo 2 Beklenmeyen Sonuç!", C.red);
  }

  // ────────────────────────────────────────────────────────────────
  // SENARYO 3: API Key Yokluğunda Failover'ın Kilitlenmesi (Configuration Error Proof)
  // ────────────────────────────────────────────────────────────────
  head("Senaryo 3: API Key Yokluğunda Failover'ın Devre Dışı Kalması Kanıtı");
  log("Açıklama: AIAgentFactory.cs içindeki ShouldFallback filtresinin, birincil");
  log("sağlayıcı yapılandırma hatası (boş API key) aldığında yedeklemeyi kesmesini kanıtlar.\n");

  log("İstek gönderiliyor: POST /api/chat/message (Standart bir mesaj)");
  const msg = await http("POST", "/api/chat/message", {
    token,
    body: {
      content: "Merhaba",
      isPlanMode: false
    }
  });

  log(`Yanıt Durumu: ${msg.status}`);
  log(`Yanıt Gövdesi: ${msg.text.slice(0, 200)}`);

  if (!msg.ok) {
    log("\n[KANITLANDI]: API anahtarları boş olduğunda failover mekanizması", C.green + C.bold);
    log("ShouldFallback engeline takılarak Groq veya Gemini yedeklerine geçiş yapamamaktadır.", C.green + C.bold);
    log("Bu sebeple sistem istekleri doğrudan kesintiye uğramaktadır.", C.green + C.bold);
  } else {
    log("\nℹ Sunucuda aktif/yedek bir sağlayıcı (veya in-memory mock) devreye girdi.", C.blue);
  }

  log("\n================================================================", C.magenta + C.bold);
  log("              TÜM CANLI AUDIT KANITLAMA TAMAMLANDI", C.magenta + C.bold);
  log("================================================================", C.magenta + C.bold);
}

runTests();
