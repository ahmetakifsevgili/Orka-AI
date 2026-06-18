#!/usr/bin/env node
// ============================================================================
// Orka AI — External API Robustness & Key Sanity Probe (SECURED)
// ============================================================================
// This script dynamically loads third-party API keys from the local .NET
// user-secrets configuration (eliminating plaintext key storage/commits).
// It performs real-world Chat Completion / generation calls ("PONG") to
// accurately detect quota exhaustion, rate limits, and config 404 errors.
// ============================================================================

import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { execFileSync } from "node:child_process";

// ── LOAD USER SECRETS ────────────────────────────────────────────────────────
async function loadUserSecrets() {
  const appData = process.env.APPDATA || (process.platform === 'win32' ? path.join(os.homedir(), 'AppData', 'Roaming') : '');
  const secretsPath = path.join(appData, 'Microsoft', 'UserSecrets', 'orka-api-secrets-2025', 'secrets.json');
  try {
    let data = await fs.readFile(secretsPath, 'utf8');
    data = data.trim().replace(/^\uFEFF/, "");
    return JSON.parse(data);
  } catch (err) {
    console.warn(`\x1b[33m[WARNING] Could not load .NET User Secrets from ${secretsPath}: ${err.message}\x1b[0m`);
    return {};
  }
}

async function loadAppConfig() {
  const merged = {};
  for (const file of [
    path.join(process.cwd(), "Orka.API", "appsettings.json"),
    path.join(process.cwd(), "Orka.API", "appsettings.Development.json")
  ]) {
    try {
      const text = await fs.readFile(file, "utf8");
      deepMerge(merged, JSON.parse(text.trim().replace(/^\uFEFF/, "")));
    } catch {
      // Optional local config; user-secrets may still be enough for non-Vertex probes.
    }
  }
  return merged;
}

function deepMerge(target, source) {
  for (const [key, value] of Object.entries(source ?? {})) {
    if (value && typeof value === "object" && !Array.isArray(value)) {
      target[key] ??= {};
      deepMerge(target[key], value);
    } else {
      target[key] = value;
    }
  }
  return target;
}

function getSecretOrConfig(secrets, config, key, fallback = "") {
  return secrets[key] || key.split(":").reduce((acc, part) => acc?.[part], config) || fallback;
}

function buildGeminiProbe(secrets, config) {
  const baseUrl = getSecretOrConfig(secrets, config, "AI:Gemini:BaseUrl", "https://generativelanguage.googleapis.com/v1beta/models");
  const model = getSecretOrConfig(secrets, config, "AI:Gemini:ModelQuiz",
    getSecretOrConfig(secrets, config, "AI:Gemini:ModelDeepPlan",
      getSecretOrConfig(secrets, config, "AI:Gemini:ModelTutor", "gemini-3.1-pro-preview")));
  const cleanModel = String(model).replace(/^models\//, "");
  const useVertex = String(getSecretOrConfig(secrets, config, "AI:Gemini:UseVertexAi", "")).toLowerCase() === "true" ||
    String(baseUrl).includes("aiplatform.googleapis.com");

  if (useVertex) {
    const token = getGcloudAccessToken();
    if (!token) return null;
    return {
      url: `${String(baseUrl).replace(/\/$/, "")}/${cleanModel}:generateContent`,
      headers: { "Authorization": `Bearer ${token}`, "Content-Type": "application/json" },
      method: "POST",
      body: JSON.stringify({ contents: [{ role: "user", parts: [{ text: "ping" }] }] })
    };
  }

  const apiKey = getSecretOrConfig(secrets, config, "AI:Gemini:ApiKey", "");
  if (!apiKey) return null;
  return {
    url: `${String(baseUrl).replace(/\/$/, "")}/${cleanModel}:generateContent?key=${apiKey}`,
    headers: { "Content-Type": "application/json" },
    method: "POST",
    body: JSON.stringify({ contents: [{ parts: [{ text: "ping" }] }] })
  };
}

function getGcloudAccessToken() {
  try {
    const command = process.platform === "win32" ? "cmd.exe" : "gcloud";
    const args = process.platform === "win32"
      ? ["/c", "gcloud.cmd", "auth", "application-default", "print-access-token"]
      : ["auth", "application-default", "print-access-token"];
    return execFileSync(command, args, { encoding: "utf8", timeout: 15000, stdio: ["ignore", "pipe", "ignore"] }).trim();
  } catch {
    return "";
  }
}

async function runSanityProbe() {
  console.log(`\x1b[1m\x1b[36m`);
  console.log(`=============================================================================`);
  console.log(`🔱 ORKA LEARNING OS — DEEP EXTERNAL API ROBUSTNESS & GENERATION PROBE`);
  console.log(`=============================================================================`);
  console.log(`\x1b[0m`);
  console.log(`Probing keys at: ${new Date().toLocaleString()}\n`);

  const secrets = await loadUserSecrets();
  const appConfig = await loadAppConfig();

  const keys = {
    GitHubModels: {
      url: "https://models.github.ai/inference/chat/completions",
      headers: {
        "Authorization": `Bearer ${secrets["AI:GitHubModels:Token"] || ""}`,
        "Content-Type": "application/json"
      },
      method: "POST",
      body: JSON.stringify({
        model: "gpt-4o-mini",
        messages: [{ role: "user", content: "ping" }],
        max_tokens: 5
      })
    },
    Groq: {
      url: "https://api.groq.com/openai/v1/chat/completions",
      headers: {
        "Authorization": `Bearer ${secrets["AI:Groq:ApiKey"] || ""}`,
        "Content-Type": "application/json"
      },
      method: "POST",
      body: JSON.stringify({
        model: "llama-3.3-70b-versatile",
        messages: [{ role: "user", content: "ping" }],
        max_tokens: 5
      })
    },
    Cerebras: {
      url: "https://api.cerebras.ai/v1/chat/completions",
      headers: {
        "Authorization": `Bearer ${secrets["AI:Cerebras:ApiKey"] || ""}`,
        "Content-Type": "application/json"
      },
      method: "POST",
      body: JSON.stringify({
        model: secrets["AI:Cerebras:Model"] || "gpt-oss-120b",
        messages: [{ role: "user", content: "ping" }],
        max_tokens: 5
      })
    },
    SambaNova: {
      url: "https://api.sambanova.ai/v1/chat/completions",
      headers: {
        "Authorization": `Bearer ${secrets["AI:SambaNova:ApiKey"] || ""}`,
        "Content-Type": "application/json"
      },
      method: "POST",
      body: JSON.stringify({
        model: secrets["AI:SambaNova:Model"] || "Meta-Llama-3.3-70B-Instruct",
        messages: [{ role: "user", content: "ping" }],
        max_tokens: 5
      })
    },
    OpenRouter: {
      url: "https://openrouter.ai/api/v1/chat/completions",
      headers: {
        "Authorization": `Bearer ${secrets["AI:OpenRouter:ApiKey"] || ""}`,
        "Content-Type": "application/json"
      },
      method: "POST",
      body: JSON.stringify({
        model: "anthropic/claude-3-5-haiku",
        messages: [{ role: "user", content: "ping" }],
        max_tokens: 5
      })
    },
    Tavily: {
      url: "https://api.tavily.com/search",
      headers: { "Content-Type": "application/json" },
      method: "POST",
      body: JSON.stringify({
        api_key: secrets["AI:Tavily:ApiKey"] || "",
        query: "Orka AI",
        max_results: 1
      })
    },
    Mistral: {
      url: "https://api.mistral.ai/v1/chat/completions",
      headers: {
        "Authorization": `Bearer ${secrets["AI:Mistral:ApiKey"] || ""}`,
        "Content-Type": "application/json"
      },
      method: "POST",
      body: JSON.stringify({
        model: secrets["AI:Mistral:Model"] || "mistral-small-latest",
        messages: [{ role: "user", content: "ping" }],
        max_tokens: 5
      })
    },
    Cohere: {
      url: "https://api.cohere.com/v1/models",
      headers: {
        "Authorization": `Bearer ${secrets["AI:Cohere:ApiKey"] || ""}`
      },
      method: "GET"
    },
    YouTube: {
      url: `https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=1&q=education&key=${secrets["AI:YouTube:ApiKey"] || ""}`,
      method: "GET"
    }
  };
  const geminiProbe = buildGeminiProbe(secrets, appConfig);
  if (geminiProbe) {
    keys.GeminiConfigured = geminiProbe;
  }

  async function probeKey(provider, config) {
    const start = Date.now();
    try {
      const response = await fetch(config.url, {
        method: config.method,
        headers: config.headers ?? {},
        body: config.body ?? null
      });
      const duration = Date.now() - start;
      const text = await response.text();
      let isOk = response.ok;
      let detail = "";

      if (!isOk) {
        try {
          const json = JSON.parse(text);
          detail = json.error?.message ?? json.message ?? JSON.stringify(json);
        } catch {
          detail = text.slice(0, 150);
        }
      }

      return {
        provider,
        status: response.status,
        ok: isOk,
        latencyMs: duration,
        detail: detail
      };
    } catch (err) {
      return {
        provider,
        status: 0,
        ok: false,
        latencyMs: Date.now() - start,
        detail: `Connection error: ${err.message}`
      };
    }
  }

  const results = [];
  for (const [provider, config] of Object.entries(keys)) {
    process.stdout.write(`  Probing \x1b[1m${provider.padEnd(15)}\x1b[0m... `);
    const res = await probeKey(provider, config);
    results.push(res);

    if (res.ok) {
      console.log(`\x1b[32m[HEALTHY]\x1b[0m Latency: \x1b[35m${res.latencyMs}ms\x1b[0m (Status: ${res.status})`);
    } else {
      console.log(`\x1b[31m[UNHEALTHY]\x1b[0m Latency: \x1b[35m${res.latencyMs}ms\x1b[0m (Status: ${res.status})`);
      console.log(`      \x1b[90mDetails: ${res.detail || "No details"}\x1b[0m`);
    }
  }

  console.log(`\n\x1b[1m\x1b[36m=============================================================================\x1b[0m`);
  console.log(`🔱 SANITY PROBE CONSOLIDATED REPORT (SECURED)`);
  console.log(`\x1b[1m\x1b[36m=============================================================================\x1b[0m`);
  
  const healthy = results.filter(r => r.ok).length;
  const unhealthy = results.filter(r => !r.ok).length;
  console.log(`Total Keys Inspected: ${results.length}`);
  console.log(`Healthy:              \x1b[32m${healthy}\x1b[0m`);
  console.log(`Unhealthy:            \x1b[31m${unhealthy}\x1b[0m`);
  
  console.log(`\nSuggested Remediation Action:`);
  results.forEach(r => {
    if (!r.ok) {
      if (r.status === 401 || r.status === 403) {
        console.log(`  * \x1b[31m${r.provider}\x1b[0m: Check for invalid, revoked or expired token (HTTP ${r.status}).`);
      } else if (r.status === 429) {
        console.log(`  * \x1b[31m${r.provider}\x1b[0m: Key has hit quota limits or rate limiting (HTTP 429).`);
      } else if (r.status === 404) {
        console.log(`  * \x1b[31m${r.provider}\x1b[0m: Model path not found. Check if the model ID is deprecated (HTTP 404).`);
      } else {
        console.log(`  * \x1b[31m${r.provider}\x1b[0m: Server or connection issue (HTTP ${r.status}). Details: ${r.detail}`);
      }
    }
  });
  console.log(`\n=============================================================================\n`);
}

runSanityProbe();
