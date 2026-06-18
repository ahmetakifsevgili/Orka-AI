const STATUS_LABELS: Record<string, string> = {
  healthy: "Sağlıklı",
  watch: "İzlemede",
  degraded: "Dikkat istiyor",
  degraded_eval: "Değerlendirme sınırlı",
  unknown: "Henüz ölçülmedi",
  ready: "Hazır",
  started: "Başladı",
  skipped: "Atlandı",
  blocked: "Engellendi",
  timeout: "Zaman aşımı",
  needs_input: "Bilgi gerekiyor",
  provider_missing: "Canlı kaynak hazır değil",
  source_retrieval_empty: "Kaynakta net cevap bulunamadı",
  citation_missing: "Citation eksik",
  citation_unsupported: "Citation kaynakla eşleşmiyor",
  low_confidence: "Güven düşük",
  source_grounded: "Kaynakla destekli",
  wiki_backed: "Wiki ile destekli",
  model_only: "Genel açıklama",
  hint_first_then_scaffold: "Önce ipucu, sonra adım adım",
  remediate: "Telafi anlatımı",
  explain: "Açıklama",
  guided_practice: "Rehberli pratik",
  challenge: "Meydan okuma",
  review: "Tekrar",
  source_grounded_answer: "Kaynaklı cevap",
  code_lab: "Kod laboratuvarı",
  visualize: "Görselleştirme",
  summarize: "Özet",
  visual: "Görsel anlatım",
  verbal: "Sözel anlatım",
  step_by_step: "Adım adım",
  example_first: "Önce örnek",
  code_first: "Önce kod",
  socratic: "Sokratik",
  review_first: "Önce tekrar",
  high: "Yüksek",
  medium: "Orta",
  low: "Düşük",
  normal: "Normal",
  confused: "Kafası karışık",
  frustrated: "Zorlanıyor",
  bored: "Sıkılmış",
  rushed: "Acelesi var",
  curious: "Meraklı",
  confident: "Kendinden emin",
  evidence_insufficient: "Kanıt yetersiz",
  not_ready: "Henüz hazır değil",
  thin: "Havuz zayıf",
};

const SAFE_UNKNOWN_STATUS = "Durum izleniyor";
const INTERNAL_STATUS_TOKENS = [
  "cohere",
  "debug",
  "gemini",
  "githubmodels",
  "groq",
  "model",
  "openrouter",
  "payload",
  "provider",
  "raw",
  "secret",
  "token",
  "trace",
];
const INTERNAL_COMPOUND_MARKERS = [
  "apikey",
  "developerprompt",
  "hiddenprompt",
  "modelid",
  "stacktrace",
  "systemprompt",
];

export function userSafeStatus(value?: string | null): string {
  if (!value) return "";
  const normalized = value.toLowerCase().trim();
  if (STATUS_LABELS[normalized]) return STATUS_LABELS[normalized];
  const matched = Object.entries(STATUS_LABELS).find(([key]) => normalized.includes(key));
  if (matched) return matched[1];
  const tokens = normalized.split(/[^a-z0-9]+/).filter(Boolean);
  const compact = tokens.join("");
  if (
    INTERNAL_STATUS_TOKENS.some((marker) => tokens.includes(marker)) ||
    INTERNAL_COMPOUND_MARKERS.some((marker) => compact.includes(marker))
  ) {
    return SAFE_UNKNOWN_STATUS;
  }

  const fallback = value.replace(/[_-]+/g, " ").replace(/\s+/g, " ").trim();
  if (!fallback || fallback.length > 64 || /[{}[\]<>:=/"'\\]/.test(fallback)) {
    return SAFE_UNKNOWN_STATUS;
  }

  return fallback;
}

export function statusTone(value?: string | null): "good" | "watch" | "bad" | "neutral" {
  const normalized = (value ?? "").toLowerCase();
  if (/(healthy|ready|source_grounded|wiki_backed|complete|success)/.test(normalized)) return "good";
  if (/(degraded|blocked|timeout|unsupported|missing|error|critical|failed)/.test(normalized)) return "bad";
  if (/(watch|low|thin|unknown|insufficient|needs_input|skipped)/.test(normalized)) return "watch";
  return "neutral";
}
