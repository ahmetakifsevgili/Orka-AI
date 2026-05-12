import type { CitationDto } from "@/lib/types";

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
