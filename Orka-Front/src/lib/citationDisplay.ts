import type { CitationDto, EvidenceQualityDto } from "@/lib/types";

export function citationScopeLabel(citation: CitationDto): string | null {
  const relation = citation.scopeRelation?.toLowerCase();
  switch (relation) {
    case "current":
      return "Bu ders";
    case "ancestor":
      return "Üst konu";
    case "descendant":
      return "Alt ders";
    case "direct-source":
      return "Seçili kaynak";
    case "unknown":
      return null;
    default:
      return null;
  }
}

export function retrievalScopeLabel(scope?: string | null): string | null {
  switch ((scope ?? "").toLowerCase()) {
    case "wiki_topic_tree":
      return "Wiki ağacı";
    case "tutor_topic_tree":
      return "Tutor kapsamı";
    case "source_direct":
      return "Doğrudan kaynak";
    default:
      return null;
  }
}

export function citationTopicLabel(citation: CitationDto): string | null {
  const title = citation.sourceTopicTitle?.trim();
  return title ? title : null;
}

export function citationPrimaryLabel(citation: CitationDto): string {
  return citation.label ?? citation.citationId ?? "Kaynak";
}

export function citationScopeSummary(citation: CitationDto): string | null {
  const parts = [
    citationTopicLabel(citation),
    citationScopeLabel(citation),
    retrievalScopeLabel(citation.retrievalScope),
  ].filter(Boolean);

  return parts.length > 0 ? parts.join(" - ") : null;
}

export function citationDisplayTitle(citation: CitationDto): string | undefined {
  return [citationPrimaryLabel(citation), citationScopeSummary(citation)]
    .filter(Boolean)
    .join(" - ") || undefined;
}

export function evidenceQualityLabel(evidenceQuality?: EvidenceQualityDto | null): string {
  const status = evidenceQuality?.status?.toLowerCase();
  if (evidenceQuality?.userSafeLabel) return evidenceQuality.userSafeLabel;
  switch (status) {
    case "strong":
      return "Kaynak güveni güçlü";
    case "partial":
      return "Kaynak güveni sınırlı";
    case "weak":
      return "Kaynak zayıf";
    case "missing":
      return "Kaynak bulunamadı";
    default:
      return "Kaynak durumu bilinmiyor";
  }
}

export function evidenceQualityDetail(evidenceQuality?: EvidenceQualityDto | null): string {
  const status = evidenceQuality?.status?.toLowerCase();
  const retrieved = evidenceQuality?.retrievedEvidenceCount ?? 0;
  const unsupported = evidenceQuality?.unsupportedCitationCount ?? 0;
  const missing = evidenceQuality?.citationMissingCount ?? 0;
  switch (status) {
    case "strong":
      return "Bu cevap mevcut kaynaklarla güçlü şekilde destekleniyor.";
    case "partial":
      return "Bu cevap mevcut kaynaklarla kısmen destekleniyor; önemli noktaları kontrol ederek kullan.";
    case "weak":
      return `Kaynak güveni sınırlı; ${unsupported + missing} citation sorunu ve ${retrieved} kaynak parçası görünüyor.`;
    case "missing":
      return "Bu yanıt için yeterli kaynak bulunamadı; kaynak eklemek yanıt kalitesini artırabilir.";
    default:
      return "Kaynak durumu için henüz yeterli veri yok.";
  }
}

export function evidenceQualityTone(evidenceQuality?: EvidenceQualityDto | null): "ready" | "watch" | "empty" {
  switch (evidenceQuality?.status?.toLowerCase()) {
    case "strong":
      return "ready";
    case "partial":
    case "weak":
      return "watch";
    default:
      return "empty";
  }
}
