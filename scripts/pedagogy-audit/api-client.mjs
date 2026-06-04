export function parseArgs(argv) {
  const parsed = {};
  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    const match = raw.match(/^--([^=]+)(?:=(.*))?$/);
    if (!match) continue;
    const next = argv[i + 1];
    if (match[2] !== undefined) parsed[match[1]] = match[2];
    else if (next && !next.startsWith("--")) {
      parsed[match[1]] = next;
      i += 1;
    } else parsed[match[1]] = "true";
  }
  return parsed;
}

export function boolArg(args, name) {
  const value = args[name];
  return value === true || value === "true" || value === "1" || value === "yes";
}

export function trimSlash(value) {
  return String(value).replace(/\/+$/, "");
}

export class ApiClient {
  constructor({ baseUrl, privacy, timeoutMs = 30000 }) {
    this.baseUrl = trimSlash(baseUrl);
    this.privacy = privacy;
    this.timeoutMs = timeoutMs;
    this.clientIp = null;
  }

  setClientIp(value) {
    this.clientIp = value;
  }

  async request(method, url, { token, body, timeoutMs = this.timeoutMs, evidenceKey } = {}) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), timeoutMs);
    const headers = { Accept: "application/json" };
    if (token) headers.Authorization = `Bearer ${token}`;
    if (this.clientIp) headers["X-Forwarded-For"] = this.clientIp;
    if (body !== undefined && body !== null) headers["Content-Type"] = "application/json";
    const started = performance.now();

    try {
      const response = await fetch(`${this.baseUrl}${url}`, {
        method,
        headers,
        body: body !== undefined && body !== null ? JSON.stringify(body) : undefined,
        signal: controller.signal,
      });
      const text = await response.text();
      const data = parseJson(text);
      const raw = data ?? text;
      return {
        key: evidenceKey,
        method,
        url,
        ok: response.ok,
        status: response.status,
        durationMs: Math.round(performance.now() - started),
        parseable: data !== null || text.length === 0,
        data,
        text,
        evidenceRef: this.privacy.evidenceRef(raw),
        redacted: this.privacy.redact(raw),
      };
    } catch (error) {
      return {
        key: evidenceKey,
        method,
        url,
        ok: false,
        status: 0,
        durationMs: Math.round(performance.now() - started),
        parseable: false,
        data: null,
        text: "",
        error: error instanceof Error ? error.message : String(error),
        evidenceRef: this.privacy.evidenceRef(error?.message ?? "request_error"),
        redacted: { error: error instanceof Error ? error.message : String(error) },
      };
    } finally {
      clearTimeout(timeout);
    }
  }
}

function parseJson(text) {
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}
