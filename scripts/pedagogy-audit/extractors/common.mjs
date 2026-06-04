export function endpointMap(steps) {
  return Object.fromEntries(steps.map((step) => [step.key, {
    ok: step.ok,
    status: step.status,
    durationMs: step.durationMs,
    parseable: step.parseable,
    ref: step.evidenceRef,
  }]));
}

export function array(value) {
  return Array.isArray(value) ? value : [];
}

export function number(value, fallback = 0) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

export function bool(value) {
  return value === true;
}

export function statusText(value) {
  if (value == null || value === "") return "unknown";
  if (typeof value !== "object") return String(value);
  for (const key of ["type", "actionType", "status", "title", "label", "name", "text", "reason"]) {
    const candidate = value[key] ?? value[capitalize(key)];
    if (candidate !== undefined && candidate !== null && candidate !== "") return String(candidate);
  }
  try {
    return JSON.stringify(value);
  } catch {
    return "object";
  }
}

export function getAny(object, paths, fallback = undefined) {
  for (const path of paths) {
    const value = getPath(object, path);
    if (value !== undefined && value !== null && value !== "") return value;
  }
  return fallback;
}

export function getPath(object, path) {
  const parts = String(path).split(".").filter(Boolean);
  let current = object;
  for (const part of parts) {
    if (current == null || typeof current !== "object") return undefined;
    current = current[part];
  }
  return current;
}

export function unique(values) {
  return [...new Set(values.filter((value) => value !== undefined && value !== null && value !== ""))];
}

export function compactObject(value) {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined));
}

function capitalize(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
