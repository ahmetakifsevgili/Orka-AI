import crypto from "node:crypto";

const DROP_KEYS = new Set([
  "token",
  "accesstoken",
  "refreshtoken",
  "idtoken",
  "authorization",
  "password",
  "apikey",
  "api_key",
  "api-key",
  "rawproviderpayload",
  "rawsourcechunk",
  "rawtoolpayload",
  "systemprompt",
  "developerprompt",
  "stacktrace",
  "localpath",
  "answerkey",
  "correctanswer",
  "ownerid",
  "userid",
  "thoughtsignature",
]);

const ID_SUFFIX_KEYS = new Set([
  "id",
  "topicid",
  "sessionid",
  "traceid",
  "tutoractiontraceid",
  "planrequestid",
  "quizrunid",
  "assessmentitemid",
  "wikipageid",
  "sourceevidencebundleid",
  "latestqualitysnapshotid",
]);

const SECRET_PATTERNS = [
  /Bearer\s+[A-Za-z0-9._-]+/i,
  /[A-Z]:\\\\/i,
  /thoughtSignature|thought_signature/i,
  /rawProviderPayload|rawSourceChunk|rawToolPayload/i,
  /stackTrace|Traceback \(most recent call last\)|System\.[A-Za-z]+Exception/i,
  /"answerKey"\s*:/i,
  /"correctAnswer"\s*:/i,
  /"token"\s*:/i,
  /"userId"\s*:/i,
  /"ownerId"\s*:/i,
];

export function createPrivacy(runId) {
  const salt = crypto.createHash("sha256").update(`orka-pedagogy-audit:${runId}`).digest("hex");
  const refs = new Map();

  function ref(prefix, value) {
    if (!value) return null;
    const key = `${prefix}:${String(value)}`;
    if (!refs.has(key)) {
      refs.set(key, `${prefix}_${hash(`${salt}:${key}`).slice(0, 8)}`);
    }
    return refs.get(key);
  }

  function redact(value, path = []) {
    if (value == null) return value;
    if (Array.isArray(value)) return value.map((item, index) => redact(item, path.concat(String(index))));
    if (typeof value === "object") {
      const output = {};
      for (const [key, child] of Object.entries(value)) {
        const normalized = normalizeKey(key);
        if (DROP_KEYS.has(normalized)) {
          output[key] = "[redacted]";
          continue;
        }
        if (ID_SUFFIX_KEYS.has(normalized) || normalized.endsWith("id")) {
          output[key] = ref(resolvePrefix(normalized), child);
          continue;
        }
        output[key] = redact(child, path.concat(key));
      }
      return output;
    }
    if (typeof value === "string") {
      return value
        .replace(/Bearer\s+[A-Za-z0-9._-]+/gi, "Bearer [redacted]")
        .replace(/[A-Z]:\\\\[^\s"]+/g, "[redacted_path]");
    }
    return value;
  }

  function evidenceRef(value) {
    return hash(JSON.stringify(redact(value))).slice(0, 12);
  }

  return { ref, redact, evidenceRef, hasPublicLeak };
}

export function hasPublicLeak(value) {
  const text = typeof value === "string" ? value : JSON.stringify(value ?? "");
  return SECRET_PATTERNS.some((pattern) => pattern.test(text));
}

export function forbiddenFieldHits(value, path = []) {
  const hits = [];
  visit(value, path, hits);
  return hits;
}

function visit(value, path, hits) {
  if (value == null) return;
  if (Array.isArray(value)) {
    value.forEach((item, index) => visit(item, path.concat(String(index)), hits));
    return;
  }
  if (typeof value !== "object") return;
  for (const [key, child] of Object.entries(value)) {
    const normalized = normalizeKey(key);
    if (DROP_KEYS.has(normalized)) {
      hits.push({ path: path.concat(key).join("."), key });
    } else {
      visit(child, path.concat(key), hits);
    }
  }
}

function normalizeKey(value) {
  return String(value ?? "").replace(/[^a-z0-9]/gi, "").toLowerCase();
}

function resolvePrefix(key) {
  if (key.includes("topic")) return "topic";
  if (key.includes("session")) return "session";
  if (key.includes("trace")) return "trace";
  if (key.includes("plan")) return "plan";
  if (key.includes("quiz")) return "quiz";
  if (key.includes("wiki")) return "wiki";
  if (key.includes("assessment")) return "item";
  return "id";
}

function hash(value) {
  return crypto.createHash("sha256").update(String(value ?? "")).digest("hex");
}
