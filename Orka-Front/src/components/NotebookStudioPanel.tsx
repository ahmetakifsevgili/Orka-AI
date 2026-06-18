import { useEffect, useMemo, useState } from "react";
import {
  BookOpen,
  BrainCircuit,
  FileText,
  Layers,
  Loader2,
  Map,
  RefreshCw,
  Sparkles,
} from "lucide-react";
import toast from "react-hot-toast";
import RichMarkdown from "./RichMarkdown";
import { NotebookStudioAPI } from "@/services/api";
import type { LearningArtifactDto, LearningNotebookPackDto, NotebookExportResultDto, NotebookSlideExportPreviewDto } from "@/lib/types";
import { userSafeStatus } from "@/lib/userSafeStatus";

interface NotebookStudioPanelProps {
  topicId: string;
  sessionId?: string | null;
  wikiPageId?: string | null;
  wikiPageTitle?: string | null;
  surface?: "wiki_page" | "source_notebook" | "milestone";
  sourceId?: string | null;
  sourceTitle?: string | null;
  sourceEvidenceStatus?: string | null;
}

const artifactActions = [
  "briefing_doc",
  "study_guide",
  "source_digest",
  "glossary",
  "timeline",
  "misconception_repair_pack",
  "mind_map",
  "uml_diagram",
  "flashcard_set",
  "review_quiz",
  "slide_deck_outline",
  "slide_export_manifest",
  "properties_panel",
  "tag_map",
  "backlink_map",
  "linked_mentions",
  "reference_map",
  "graph_view",
  "template_set",
  "search_filter_index",
  "audio_script",
  "audio_transcript",
  "caption_track",
  "narration_script",
  "audio_overview",
];

const primaryArtifactActions = [
  "study_guide",
  "source_digest",
  "review_quiz",
  "audio_overview",
] as const;

const secondaryArtifactActions = artifactActions.filter(
  (type) => !primaryArtifactActions.includes(type as (typeof primaryArtifactActions)[number])
);

const artifactLabels: Record<string, string> = {
  study_guide: "Study guide",
  briefing_doc: "Brief",
  source_digest: "Source digest",
  misconception_repair_pack: "Repair notes",
  worked_example_set: "Ornek seti",
  retrieval_card_set: "Hatirlama kartlari",
  audio_overview: "Audio overview",
  audio_script: "Audio script",
  audio_transcript: "Audio transcript",
  caption_track: "Caption track",
  glossary: "Glossary",
  timeline: "Timeline",
  mind_map: "Concept map",
  uml_diagram: "UML / Mermaid",
  flashcard_set: "Flashcard seti",
  review_quiz: "Mikro test",
  slide_deck_outline: "Deck outline",
  video_ready_package: "Video readiness",
  slide_export_manifest: "Deck export manifest",
  narration_script: "Narration script",
  visual_instruction_set: "Visual instructions",
  media_accessibility_note: "Media accessibility",
  properties_panel: "Properties",
  tag_map: "Tags",
  backlink_map: "Backlinks",
  linked_mentions: "Linked mentions",
  reference_map: "Block refs",
  graph_view: "Graph view",
  template_set: "Templates",
  search_filter_index: "Search/filter",
  milestone_review: "Milestone ozeti",
};

const statusTone = (status?: string | null) => {
  switch ((status ?? "").toLowerCase()) {
    case "ready":
    case "source_grounded":
    case "mixed":
    case "wiki_backed":
      return "border-[#a7e879]/25 bg-[#a7e879]/10 text-[#c9f5a9]";
    case "stale":
    case "degraded":
    case "evidence_insufficient":
      return "border-[#dac17a]/25 bg-[#dac17a]/10 text-[#e6d49b]";
    default:
      return "border-white/[0.08] bg-white/[0.045] text-[#aeb6b2]";
  }
};

const parseArtifactJson = (artifact: LearningArtifactDto): Record<string, unknown> => {
  if (!artifact.contentJson) return {};
  try {
    const parsed = JSON.parse(artifact.contentJson);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {};
  } catch {
    return {};
  }
};

const getAudioJobId = (artifact: LearningArtifactDto): string | null => {
  const value = parseArtifactJson(artifact).audioOverviewJobId;
  return typeof value === "string" && value.length > 0 ? value : null;
};

const getArtifactStatus = (artifact: LearningArtifactDto): string | null => {
  const value = parseArtifactJson(artifact).status;
  return typeof value === "string" && value.length > 0 ? value : null;
};

const getArtifactMetaValue = (artifact: LearningArtifactDto, key: string): string | null => {
  const value = parseArtifactJson(artifact)[key];
  return typeof value === "string" && value.length > 0 ? value : null;
};

const isMediaExportArtifact = (artifact: LearningArtifactDto) =>
  [
    "slide_export_manifest",
    "visual_instruction_set",
  ].includes(artifact.artifactType);

const renderableContent = (artifact: LearningArtifactDto) => {
  if (artifact.renderFormat === "mermaid") {
    return `\`\`\`mermaid\n${artifact.safeContent}\n\`\`\``;
  }
  return artifact.safeContent;
};

const labelFor = (value: string) => artifactLabels[value] ?? value.replaceAll("_", " ");

const surfaceLabel = (value?: string | null) => {
  const normalized = (value ?? "").toLowerCase();
  if (normalized === "orkalm" || normalized === "source_notebook") return "Source notebook";
  if (normalized === "wiki" || normalized === "wiki_page") return "Wiki page";
  if (normalized === "milestone") return "Milestone";
  if (normalized === "source_collection") return "Source collection";
  if (normalized === "surface_unknown" || normalized === "context_unknown") return "Not set yet";
  return userSafeStatus(value);
};

const READABLE_STATUS_LABELS: Record<string, string> = {
  preview_pending: "Preview pending",
  scoped_preview_pending: "Scoped preview pending",
  pptx_not_enabled: "PPTX not enabled",
  local_proof_available: "Local proof available",
  text_fallback: "Text fallback",
  not_applicable: "Not applicable",
  slide_preview: "Slide preview",
  evidence_unknown: "Evidence not measured",
  usable: "Usable",
};

const safeStatusLabel = (value?: string | null) => {
  if (!value) return "Not set yet";
  const normalized = value.toLowerCase().trim();
  return READABLE_STATUS_LABELS[normalized] ?? userSafeStatus(value) ?? "Not set yet";
};

const displayArtifactText = (value?: string | number | null) => {
  if (value === null || value === undefined) return "";
  return surfaceLabel(String(value))
    .replace(/\bbriefing\b/gi, "quick summary")
    .replace(/cross[-_\s]?surface/gi, "outside workspace")
    .replace(/\bcontract\b/gi, "map");
};

type ArtifactTerm = {
  term: string;
  description?: string;
};

type SlidePreviewItem = {
  order?: number;
  title?: string;
  bullets?: string[];
  speakerNotes?: string;
  sourceLabel?: string | null;
  visualSuggestion?: string;
  checkpointQuestion?: string;
  misconceptionWarning?: string | null;
  accessibilitySummary?: string;
};

type ArtifactPropertyRow = {
  key: string;
  label?: string;
  value: string;
  status?: string;
};

type ArtifactGraphNode = {
  id: string;
  label: string;
  nodeType?: string;
  surface?: string;
  status?: string;
};

type ArtifactGraphEdge = {
  sourceId: string;
  targetId: string;
  edgeType?: string;
  scope?: string;
  crossSurface?: boolean;
};

type ArtifactBacklinkRow = {
  source: string;
  target: string;
  linkType?: string;
  surface?: string;
  status?: string;
};

type ArtifactMentionRow = {
  term: string;
  mentionScope?: string;
  source?: string;
  status?: string;
};

type ArtifactReferenceRow = {
  refType?: string;
  label: string;
  scope?: string;
  status?: string;
};

type ArtifactTemplateRow = {
  templateKey: string;
  title: string;
  appliesTo?: string;
  defaultArtifactType?: string;
};

type ArtifactSearchFilterRow = {
  filterKey: string;
  label?: string;
  value: string;
  surface?: string;
};

type ArtifactExportPreviewRow = {
  format: string;
  label?: string;
  status?: string;
};

const readString = (record: Record<string, unknown>, key: string): string | null => {
  const value = record[key];
  return typeof value === "string" && value.length > 0 ? value : null;
};

const readBoolean = (record: Record<string, unknown>, key: string): boolean | null => {
  const value = record[key];
  return typeof value === "boolean" ? value : null;
};

const readStringArray = (record: Record<string, unknown>, key: string): string[] => {
  const value = record[key];
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string" && item.length > 0) : [];
};

const readObjectArray = (record: Record<string, unknown>, key: string): Record<string, unknown>[] => {
  const value = record[key];
  return Array.isArray(value)
    ? value.filter((item): item is Record<string, unknown> => Boolean(item) && typeof item === "object" && !Array.isArray(item))
    : [];
};

const fieldString = (entry: Record<string, unknown>, key: string, fallback = ""): string => {
  const value = entry[key] ?? entry[key.charAt(0).toUpperCase() + key.slice(1)];
  return typeof value === "string" && value.length > 0 ? value : fallback;
};

const fieldBoolean = (entry: Record<string, unknown>, key: string): boolean | undefined => {
  const value = entry[key] ?? entry[key.charAt(0).toUpperCase() + key.slice(1)];
  return typeof value === "boolean" ? value : undefined;
};

const readTerms = (record: Record<string, unknown>): ArtifactTerm[] => {
  const value = record.terms;
  if (!Array.isArray(value)) return [];
  const terms: ArtifactTerm[] = [];
  for (const item of value) {
    if (!item || typeof item !== "object") continue;
    const entry = item as Record<string, unknown>;
    const term = typeof entry.term === "string" ? entry.term : typeof entry.Term === "string" ? entry.Term : null;
    if (!term) continue;
    const description = typeof entry.description === "string" ? entry.description : typeof entry.Description === "string" ? entry.Description : undefined;
    terms.push({ term, description });
  }
  return terms;
};

const readPropertyRows = (record: Record<string, unknown>): ArtifactPropertyRow[] =>
  readObjectArray(record, "properties").flatMap((entry) => {
    const key = fieldString(entry, "key");
    const value = fieldString(entry, "value");
    if (!key || !value) return [];
    return [{ key, label: fieldString(entry, "label", key), value, status: fieldString(entry, "status") }];
  });

const readGraphNodes = (record: Record<string, unknown>): ArtifactGraphNode[] =>
  readObjectArray(record, "graphNodes").flatMap((entry) => {
    const id = fieldString(entry, "id");
    const label = fieldString(entry, "label");
    if (!id || !label) return [];
    return [{ id, label, nodeType: fieldString(entry, "nodeType"), surface: fieldString(entry, "surface"), status: fieldString(entry, "status") }];
  });

const readGraphEdges = (record: Record<string, unknown>): ArtifactGraphEdge[] =>
  readObjectArray(record, "graphEdges").flatMap((entry) => {
    const sourceId = fieldString(entry, "sourceId");
    const targetId = fieldString(entry, "targetId");
    if (!sourceId || !targetId) return [];
    return [{ sourceId, targetId, edgeType: fieldString(entry, "edgeType"), scope: fieldString(entry, "scope"), crossSurface: fieldBoolean(entry, "crossSurface") }];
  });

const readBacklinks = (record: Record<string, unknown>): ArtifactBacklinkRow[] =>
  readObjectArray(record, "backlinks").flatMap((entry) => {
    const source = fieldString(entry, "source");
    const target = fieldString(entry, "target");
    if (!source || !target) return [];
    return [{ source, target, linkType: fieldString(entry, "linkType"), surface: fieldString(entry, "surface"), status: fieldString(entry, "status") }];
  });

const readLinkedMentions = (record: Record<string, unknown>): ArtifactMentionRow[] =>
  readObjectArray(record, "linkedMentions").flatMap((entry) => {
    const term = fieldString(entry, "term");
    if (!term) return [];
    return [{ term, mentionScope: fieldString(entry, "mentionScope"), source: fieldString(entry, "source"), status: fieldString(entry, "status") }];
  });

const readReferenceRows = (record: Record<string, unknown>): ArtifactReferenceRow[] =>
  readObjectArray(record, "blockReferences").flatMap((entry) => {
    const label = fieldString(entry, "label");
    if (!label) return [];
    return [{ label, refType: fieldString(entry, "refType"), scope: fieldString(entry, "scope"), status: fieldString(entry, "status") }];
  });

const readTemplateRows = (record: Record<string, unknown>): ArtifactTemplateRow[] =>
  readObjectArray(record, "templates").flatMap((entry) => {
    const templateKey = fieldString(entry, "templateKey");
    const title = fieldString(entry, "title");
    if (!templateKey || !title) return [];
    return [{ templateKey, title, appliesTo: fieldString(entry, "appliesTo"), defaultArtifactType: fieldString(entry, "defaultArtifactType") }];
  });

const readSearchFilters = (record: Record<string, unknown>): ArtifactSearchFilterRow[] =>
  readObjectArray(record, "searchFilters").flatMap((entry) => {
    const filterKey = fieldString(entry, "filterKey");
    const value = fieldString(entry, "value");
    if (!filterKey || !value) return [];
    return [{ filterKey, label: fieldString(entry, "label", filterKey), value, surface: fieldString(entry, "surface") }];
  });

const readExportPreviewRows = (record: Record<string, unknown>): ArtifactExportPreviewRow[] =>
  readObjectArray(record, "exportPreview").flatMap((entry) => {
    const format = fieldString(entry, "format");
    if (!format) return [];
    return [{ format, label: fieldString(entry, "label", format), status: fieldString(entry, "status") }];
  });

const readSlides = (record: Record<string, unknown>): SlidePreviewItem[] => {
  const value = record.slides;
  if (!Array.isArray(value)) return [];
  const slides: SlidePreviewItem[] = [];
  for (const item of value) {
    if (!item || typeof item !== "object") continue;
    const entry = item as Record<string, unknown>;
    const title = typeof entry.title === "string" ? entry.title : undefined;
    if (!title) continue;
    slides.push({
      order: typeof entry.order === "number" ? entry.order : undefined,
      title,
      bullets: Array.isArray(entry.bullets) ? entry.bullets.filter((bullet): bullet is string => typeof bullet === "string") : [],
      speakerNotes: typeof entry.speakerNotes === "string" ? entry.speakerNotes : undefined,
      sourceLabel: typeof entry.sourceLabel === "string" ? entry.sourceLabel : null,
      visualSuggestion: typeof entry.visualSuggestion === "string" ? entry.visualSuggestion : undefined,
      checkpointQuestion: typeof entry.checkpointQuestion === "string" ? entry.checkpointQuestion : undefined,
      misconceptionWarning: typeof entry.misconceptionWarning === "string" ? entry.misconceptionWarning : null,
      accessibilitySummary: typeof entry.accessibilitySummary === "string" ? entry.accessibilitySummary : undefined,
    });
  }
  return slides;
};

export default function NotebookStudioPanel({
  topicId,
  sessionId,
  wikiPageId,
  wikiPageTitle,
  surface,
  sourceId,
  sourceTitle,
  sourceEvidenceStatus,
}: NotebookStudioPanelProps) {
  const [packs, setPacks] = useState<LearningNotebookPackDto[]>([]);
  const [selected, setSelected] = useState<LearningNotebookPackDto | null>(null);
  const [selectedArtifactId, setSelectedArtifactId] = useState<string | null>(null);
  const [exportPreview, setExportPreview] = useState<NotebookSlideExportPreviewDto | null>(null);
  const [exportResult, setExportResult] = useState<NotebookExportResultDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showAdvancedStudio, setShowAdvancedStudio] = useState(false);



  const refresh = async () => {
    setLoading(true);
    setError(null);
    try {
      const isSourceSurface = surface === "source_notebook" && !!sourceId;
      const isSourceCollectionSurface = surface === "source_notebook" && !sourceId;
      const result = await NotebookStudioAPI.listPacks(
        topicId,
        sessionId ?? undefined,
        surface === "source_notebook" ? undefined : wikiPageId ?? undefined,
        isSourceSurface
          ? { surface: "source", sourceId }
          : isSourceCollectionSurface
          ? { surface: "source_collection" }
          : {}
      );
      setPacks(result.items ?? []);
      setSelected((current) => {
        if (!result.items?.length) return null;
        return result.items.find((pack) => pack.id === current?.id) ?? result.items[0];
      });
    } catch {
      setError("Artifact sets could not be loaded. Try again later.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void refresh();
  }, [topicId, sessionId, wikiPageId, surface, sourceId]);

  const buildPack = async () => {
    setLoading(true);
    setError(null);
    try {
      const pack = surface === "source_notebook"
        ? sourceId
          ? await NotebookStudioAPI.buildSourcePack(sourceId, {
            sessionId: sessionId ?? undefined,
            sourceId,
            sourceSurface: "source",
            packType: "source_digest",
            includeArtifacts: true,
          })
          : await NotebookStudioAPI.buildTopicSourcePack(topicId, {
            sessionId: sessionId ?? undefined,
            sourceSurface: "source_collection",
            packType: "source_notebook",
            includeArtifacts: true,
          })
        : wikiPageId
        ? await NotebookStudioAPI.buildWikiPagePack(wikiPageId, {
            sessionId: sessionId ?? undefined,
            wikiPageId,
            packType: "wiki_page_review",
            includeArtifacts: true,
          })
        : await NotebookStudioAPI.buildMilestonePack(topicId, {
            sessionId: sessionId ?? undefined,
            packType: "milestone_review",
            includeArtifacts: true,
          });
      setSelected(pack);
      setSelectedArtifactId(pack.artifacts[0]?.id ?? null);
      await refresh();
      toast.success("Artifact set is ready.");
    } catch {
      setError("Artifact set could not be prepared. Source or Wiki readiness may be limited.");
      toast.error("Artifact set could not be prepared.");
    } finally {
      setLoading(false);
    }
  };

  const buildArtifact = async (artifactType: string) => {
    if (!selected) return;
    setLoading(true);
    setError(null);
    try {
      const artifact = await NotebookStudioAPI.buildArtifact(selected.id, { artifactType });
      const pack = await NotebookStudioAPI.getPack(selected.id);
      setSelected(pack);
      setSelectedArtifactId(artifact.id);
      setPacks((items) => items.map((item) => (item.id === pack.id ? pack : item)));
      toast.success(`${artifact.title} hazirlandi.`);
    } catch {
      setError("Artifact could not be generated. Safety, source, or set readiness may be limited.");
      toast.error("Artifact could not be generated.");
    } finally {
      setLoading(false);
    }
  };

  const loadExportPreview = async (packId: string) => {
    try {
      const preview = await NotebookStudioAPI.getExportPreview(packId);
      setSelected((curr) => {
        if (curr?.id === packId) {
          setExportPreview(preview);
        }
        return curr;
      });
    } catch (err) {
      setExportPreview(null);
      console.error("Preview load failure:", err);
    }
  };

  const exportPack = async (format: "markdown" | "html" | "manifest_only" | "pptx_local_proof") => {
    if (!selected) return;
    setLoading(true);
    setError(null);
    try {
      const result = await NotebookStudioAPI.exportPack(selected.id, { format });
      setExportResult(result);
      setExportPreview(result.preview);
      if (result.status === "unsupported") {
        toast("Bu export formati henuz etkin degil; guvenli fallback gosteriliyor.");
      } else {
        toast.success("Export paketi hazirlandi.");
      }
    } catch {
      setError("Export paketi hazirlanamadi. Slide outline veya pack durumu eksik olabilir.");
      toast.error("Export paketi hazirlanamadi.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setExportResult(null);
    if (selected?.id) void loadExportPreview(selected.id);
    else setExportPreview(null);
  }, [selected?.id]);

  const artifacts = selected?.artifacts ?? [];
  const selectedArtifact = useMemo(
    () => artifacts.find((artifact) => artifact.id === selectedArtifactId) ?? artifacts[0] ?? null,
    [artifacts, selectedArtifactId]
  );

  const groupedArtifacts = useMemo(() => {
    const groups: Record<string, LearningArtifactDto[]> = {
      "Study notes": [],
      "Sources and repair": [],
      "Practice": [],
      "Graph and metadata": [],
      "Media and export": [],
    };
    for (const artifact of artifacts) {
      if (["study_guide", "briefing_doc", "milestone_review", "glossary", "timeline"].includes(artifact.artifactType)) groups["Study notes"].push(artifact);
      else if (["source_digest", "misconception_repair_pack", "worked_example_set"].includes(artifact.artifactType)) groups["Sources and repair"].push(artifact);
      else if (["retrieval_card_set", "flashcard_set", "review_quiz"].includes(artifact.artifactType)) groups["Practice"].push(artifact);
      else if (["mind_map", "uml_diagram", "properties_panel", "tag_map", "backlink_map", "linked_mentions", "reference_map", "graph_view", "template_set", "search_filter_index"].includes(artifact.artifactType)) groups["Graph and metadata"].push(artifact);
      else if (["audio_script", "audio_transcript", "caption_track", "narration_script", "audio_overview"].includes(artifact.artifactType)) groups["Media and export"].push(artifact);
      else groups["Media and export"].push(artifact);
    }
    return groups;
  }, [artifacts]);

  const primaryArtifacts = artifacts.filter((artifact) =>
    primaryArtifactActions.includes(artifact.artifactType as (typeof primaryArtifactActions)[number])
  );
  const advancedArtifacts = artifacts.filter(
    (artifact) => !primaryArtifactActions.includes(artifact.artifactType as (typeof primaryArtifactActions)[number])
  );
  const surfaceContextLabel = surface === "source_notebook"
    ? sourceTitle || "Kaynak koleksiyonu"
    : wikiPageTitle || "Konu not defteri";
  const buildPackLabel = surface === "source_notebook"
    ? "Build source set"
    : wikiPageId ? "Sayfa defteri oluştur" : "Konu defteri oluştur";

  if (primaryArtifactActions.length > 0) return (
    <section className="rounded-2xl border border-[#263332] bg-[#0b0f0e]/92 p-4 text-[#eef2ee] shadow-[0_18px_60px_rgba(0,0,0,0.24)]">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#7edfd4]">
            <Layers className="h-4 w-4" />
            Notebook Studio
            {loading && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
          </div>
          <h4 className="mt-1 truncate text-lg font-semibold text-[#f7faf7]">{surfaceContextLabel}</h4>
          <p className="mt-1 max-w-2xl text-xs leading-5 text-[#8d9894]">
            Calisma paketi, sesli anlatim ve export burada baglamsal aractir; ana is secili belgeyi okumak ve kanitini gormek.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={refresh}
            disabled={loading}
            className="rounded-lg border border-white/[0.09] bg-white/[0.035] p-2 text-[#9aa3a0] transition hover:border-white/[0.16] hover:text-white disabled:opacity-40"
            aria-label="Notebook Studio yenile"
          >
            <RefreshCw className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={buildPack}
            disabled={loading}
            className="inline-flex items-center gap-1.5 rounded-lg bg-[#e9f0ea] px-3 py-2 text-xs font-black text-[#0b0f0e] transition hover:bg-white disabled:opacity-40"
          >
            <Sparkles className="h-3.5 w-3.5" />
            {buildPackLabel}
          </button>
        </div>
      </div>

      {(surface === "source_notebook" || wikiPageTitle) && (
        <div className="mb-4 flex flex-wrap items-center gap-2 rounded-xl border border-white/[0.08] bg-white/[0.035] px-3 py-2 text-xs font-semibold text-[#b7c1bd]">
          <span>{surface === "source_notebook" ? "Kaynak defteri" : "Wiki sayfasi"}</span>
          <span className="text-[#5e6865]">/</span>
          <span className="text-[#eef2ee]">{surfaceContextLabel}</span>
          <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(sourceEvidenceStatus || selected?.evidenceStatus)}`}>
            {safeStatusLabel(sourceEvidenceStatus || selected?.evidenceStatus || "evidence_unknown")}
          </span>
        </div>
      )}

      {error && (
        <div className="mb-4 rounded-xl border border-rose-500/22 bg-rose-500/10 px-3 py-2 text-xs font-semibold leading-5 text-rose-200">
          {error}
        </div>
      )}

      {selected ? (
        <div className="grid gap-4 xl:grid-cols-[260px_minmax(0,1fr)_300px]">
          <aside className="space-y-3">
            <div className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-3">
              <div className="mb-2 flex items-center justify-between gap-2">
                <span className="text-[10px] font-black uppercase tracking-[0.18em] text-[#7b8582]">Paketler</span>
                <span className="rounded-full bg-white/[0.055] px-2 py-0.5 text-[10px] font-bold text-[#9aa3a0]">{packs.length}</span>
              </div>
              <div className="max-h-44 space-y-1 overflow-y-auto pr-1 sidebar-scrollbar">
                {packs.map((pack) => (
                  <button
                    key={pack.id}
                    type="button"
                    onClick={() => {
                      setSelected(pack);
                      setSelectedArtifactId(pack.artifacts[0]?.id ?? null);
                    }}
                    className={`w-full rounded-lg border px-3 py-2 text-left transition ${
                      selected.id === pack.id
                        ? "border-[#7edfd4]/35 bg-[#7edfd4]/10 text-[#f7faf7]"
                        : "border-transparent bg-transparent text-[#9aa3a0] hover:bg-white/[0.045] hover:text-[#f7faf7]"
                    }`}
                  >
                    <span className="block truncate text-xs font-black">{pack.title || surfaceLabel(pack.packType)}</span>
                    <span className="mt-0.5 block truncate text-[10px] font-semibold text-[#6f7a76]">
                      {pack.sourceTitle || pack.wikiPageTitle || surfaceLabel(pack.packType)}
                    </span>
                  </button>
                ))}
              </div>
            </div>

            <div className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-3">
              <div className="mb-2 flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#7b8582]">
                <FileText className="h-3.5 w-3.5" />
                Ciktilar
              </div>
              <div className="space-y-1.5">
                {(primaryArtifacts.length > 0 ? primaryArtifacts : artifacts.slice(0, 4)).map((artifact) => (
                  <button
                    key={artifact.id}
                    type="button"
                    onClick={() => setSelectedArtifactId(artifact.id)}
                    className={`w-full rounded-lg border px-3 py-2 text-left transition ${
                      selectedArtifact?.id === artifact.id
                        ? "border-[#e9f0ea]/24 bg-[#e9f0ea]/10 text-[#f7faf7]"
                        : "border-transparent bg-transparent text-[#9aa3a0] hover:bg-white/[0.045] hover:text-[#f7faf7]"
                    }`}
                  >
                    <span className="block truncate text-xs font-black">{labelFor(artifact.artifactType)}</span>
                    <span className="mt-0.5 block truncate text-[10px] font-semibold text-[#6f7a76]">{artifact.title}</span>
                  </button>
                ))}
                {advancedArtifacts.length > 0 && (
                  <details open className="pt-1">
                    <summary className="cursor-pointer rounded-lg px-3 py-2 text-[11px] font-bold text-[#7b8582] transition hover:bg-white/[0.045] hover:text-[#dce4df]">
                      Gelismis ciktilar ({advancedArtifacts.length})
                    </summary>
                    <div className="mt-1 space-y-1">
                      {advancedArtifacts.map((artifact) => (
                        <button
                          key={artifact.id}
                          type="button"
                          aria-label={artifact.title}
                          onClick={() => setSelectedArtifactId(artifact.id)}
                          className="w-full rounded-lg px-3 py-1.5 text-left text-[11px] font-semibold text-[#8d9894] transition hover:bg-white/[0.045] hover:text-[#f7faf7]"
                        >
                          {artifact.title}
                        </button>
                      ))}
                    </div>
                  </details>
                )}
              </div>
            </div>
          </aside>

          <article className="min-h-[520px] overflow-hidden rounded-2xl border border-[#d9dfd8]/14 bg-[#f5f2e9] text-[#172033] shadow-[0_24px_80px_rgba(0,0,0,0.22)]">
            <div className="border-b border-[#172033]/10 px-6 py-5">
              <div className="flex flex-wrap items-center gap-2">
                <span className="rounded-full border border-[#172033]/10 bg-white/68 px-2.5 py-1 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">
                  {selectedArtifact ? labelFor(selectedArtifact.artifactType) : surfaceLabel(selected.packType)}
                </span>
                <span className={`rounded-full border px-2.5 py-1 text-[10px] font-bold ${statusTone(selectedArtifact?.sourceBasis ?? selected.evidenceStatus)}`}>
                  {safeStatusLabel(selectedArtifact?.sourceBasis ?? selected.evidenceStatus)}
                </span>
              </div>
              <h5 className="mt-3 text-2xl font-black tracking-tight text-[#172033]">
                {selectedArtifact?.title || selected.title}
              </h5>
              {selectedArtifact && <ArtifactSurfaceSummary artifact={selectedArtifact} />}
              <p className="mt-2 max-w-2xl text-sm leading-6 text-[#52606a]">
                {selected.summary || "Bu defter secili kaynak veya wiki sayfasindan guvenli calisma ciktisi uretir."}
              </p>
            </div>
            <div className="max-h-[620px] overflow-y-auto px-6 py-5 sidebar-scrollbar">
              {selectedArtifact ? (
                <>
                  {selectedArtifact.artifactType.startsWith("audio") && (
                    <PhaseSevenAudioNotice artifact={selectedArtifact} />
                  )}
                  {isMediaExportArtifact(selectedArtifact) && (
                    <div className="mb-4 grid gap-2 sm:grid-cols-2">
                      <MediaMeta label="Durum" value={getArtifactStatus(selectedArtifact) ?? selectedArtifact.artifactStatus} tone={statusTone(getArtifactStatus(selectedArtifact) ?? selectedArtifact.artifactStatus)} />
                      <MediaMeta label="Kaynak" value={safeStatusLabel(getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis)} tone={statusTone(getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis)} />
                    </div>
                  )}
                  <details open className="mt-5 rounded-xl border border-[#172033]/10 bg-white/55 p-3">
                    <summary className="cursor-pointer text-xs font-black uppercase tracking-[0.14em] text-[#667085]">
                      Kanit, graph ve metadata
                    </summary>
                    <div className="mt-3">
                      <ProfessionalArtifactPreview artifact={selectedArtifact} />
                    </div>
                  </details>
                  <RichMarkdown
                    content={renderableContent(selectedArtifact)}
                    className="mt-4 prose prose-sm max-w-none text-sm leading-6 prose-headings:text-[#172033] prose-p:text-[#344054] prose-li:text-[#344054] prose-p:my-2 prose-li:my-1"
                  />
                </>
              ) : (
                <div className="rounded-xl border border-dashed border-[#172033]/14 bg-white/44 px-4 py-6 text-sm text-[#667085]">
                  Bu pakette henuz cikti yok. Sagdaki Studio alanindan ilk calisma notunu olustur.
                </div>
              )}
            </div>
          </article>

          <aside className="space-y-3">
            <div className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-3">
              <div className="mb-2 flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#7b8582]">
                <BrainCircuit className="h-3.5 w-3.5" />
                Kanit ve yon
              </div>
              <div className="grid gap-2">
                <SignalBox label="Kaynak" value={safeStatusLabel(selected.sourceReadiness)} />
                <SignalBox label="Kanit" value={safeStatusLabel(selected.evidenceStatus)} />
                <SignalBox label="Paket" value={safeStatusLabel(selected.packStatus)} />
              </div>
              {selected.warnings.length > 0 && (
                <p className="mt-3 rounded-lg border border-amber-500/20 bg-amber-500/10 px-3 py-2 text-[11px] font-semibold leading-5 text-[#dac17a]">
                  {selected.warnings.slice(0, 2).join(" - ")}
                </p>
              )}
            </div>

            {selected.nextActions.length > 0 && (
              <div className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-3">
                <div className="mb-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#7b8582]">Siradaki adim</div>
                <div className="space-y-1.5">
                  {selected.nextActions.slice(0, 3).map((action) => (
                    <div key={`${action.actionType}-${action.userSafeLabel}`} className="rounded-lg border border-white/[0.07] bg-white/[0.035] px-3 py-2 text-xs font-semibold leading-5 text-[#dce4df]">
                      {action.userSafeLabel}
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-3">
              <div className="mb-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#7b8582]">Hizli uret</div>
              <div className="grid gap-2">
                {primaryArtifactActions.map((type) => (
                  <button
                    key={type}
                    type="button"
                    onClick={() => buildArtifact(type)}
                    disabled={loading}
                    className="rounded-lg border border-white/[0.08] bg-white/[0.04] px-3 py-2 text-left text-xs font-black text-[#e9f0ea] transition hover:border-[#7edfd4]/30 hover:bg-[#7edfd4]/10 disabled:opacity-40"
                  >
                    {labelFor(type)}
                  </button>
                ))}
              </div>
              <details open className="mt-3">
                <summary className="cursor-pointer rounded-lg px-2 py-1.5 text-[11px] font-bold text-[#7b8582] transition hover:bg-white/[0.045] hover:text-[#dce4df]">
                  Gelismis uretim ve export
                </summary>
                <div className="mt-2 flex flex-wrap gap-2">
                  {secondaryArtifactActions.slice(0, showAdvancedStudio ? secondaryArtifactActions.length : 8).map((type) => (
                    <button
                      key={type}
                      type="button"
                      onClick={() => buildArtifact(type)}
                      disabled={loading}
                      className="rounded-full border border-white/[0.08] bg-white/[0.035] px-2.5 py-1 text-[10px] font-bold text-[#9aa3a0] transition hover:border-white/[0.16] hover:text-[#f7faf7] disabled:opacity-40"
                    >
                      {labelFor(type)}
                    </button>
                  ))}
                  {secondaryArtifactActions.length > 8 && (
                    <button
                      type="button"
                      onClick={() => setShowAdvancedStudio((value) => !value)}
                      className="rounded-full border border-white/[0.08] bg-white/[0.035] px-2.5 py-1 text-[10px] font-bold text-[#7edfd4] transition hover:border-[#7edfd4]/30"
                    >
                      {showAdvancedStudio ? "Daha az" : "Hepsini goster"}
                    </button>
                  )}
                </div>
                <div className="mt-3 grid gap-2">
                  <button type="button" onClick={() => selected && loadExportPreview(selected.id)} disabled={loading} className="rounded-lg border border-white/[0.08] bg-white/[0.035] px-3 py-2 text-left text-[11px] font-bold text-[#dce4df] transition hover:bg-white/[0.06] disabled:opacity-40">Preview</button>
                  <button type="button" onClick={() => exportPack("markdown")} disabled={loading} className="rounded-lg border border-white/[0.08] bg-white/[0.035] px-3 py-2 text-left text-[11px] font-bold text-[#dce4df] transition hover:bg-white/[0.06] disabled:opacity-40">Markdown al</button>
                  <button type="button" onClick={() => exportPack("html")} disabled={loading} className="rounded-lg border border-white/[0.08] bg-white/[0.035] px-3 py-2 text-left text-[11px] font-bold text-[#dce4df] transition hover:bg-white/[0.06] disabled:opacity-40">Safe HTML al</button>
                </div>
              </details>
            </div>

            {(exportPreview || exportResult) && (
              <div className="rounded-xl border border-white/[0.08] bg-white/[0.03] p-3">
                <div className="mb-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#7b8582]">Export durumu</div>
                <div className="grid gap-2">
                  <SignalBox label="Readiness" value={safeStatusLabel(exportResult?.exportReadiness ?? exportPreview?.exportReadiness ?? "preview_pending")} />
                  <SignalBox label="Format" value={safeStatusLabel(exportResult?.format ?? "slide_preview")} />
                  <SignalBox label="Export scope key" value={String(exportResult?.exportScope ?? exportPreview?.exportScope ?? "scoped_preview_pending").replaceAll("_", " ").toLowerCase()} />
                </div>
                {exportResult && (
                  <pre className="mt-2 max-h-28 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-[#050706] p-2 text-[10px] leading-4 text-[#cbd5d1]">
                    {exportResult.content.slice(0, 900)}
                  </pre>
                )}
              </div>
            )}
          </aside>
        </div>
      ) : (
        <div className="rounded-xl border border-dashed border-white/[0.12] bg-white/[0.025] px-4 py-6 text-sm leading-6 text-[#9aa3a0]">
          Bu baglam icin henuz defter yok. Once kaynak veya wiki sayfasi hazir olsun, sonra tek bir calisma defteri olustur.
        </div>
      )}
    </section>
  );

  return (
    <section className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Layers className="h-4 w-4 text-[#6ed7ce]" />
          <span className="text-xs font-semibold uppercase tracking-widest text-[#c8cfca]">Notebook Studio</span>
          {loading && <Loader2 className="h-3.5 w-3.5 animate-spin text-[#6ed7ce]" />}
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={refresh}
            disabled={loading}
            className="rounded-lg border border-white/[0.08] bg-white/[0.045] p-1.5 text-[#9aa3a0] transition hover:text-white disabled:opacity-40"
            aria-label="Refresh Notebook Studio artifacts"
          >
            <RefreshCw className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={buildPack}
            disabled={loading}
            className="inline-flex items-center gap-1.5 rounded-lg bg-[#f4f6f3] px-3 py-1.5 text-xs font-semibold text-[#070809] transition hover:bg-[#dfe5df] disabled:opacity-40"
          >
            <Sparkles className="h-3.5 w-3.5" />
            {surface === "source_notebook" ? "Build source set" : wikiPageId ? "Build page set" : "Build milestone set"}
          </button>
        </div>
      </div>

      {surface === "source_notebook" && (
        <div className="mb-3 rounded-lg border border-white/[0.08] bg-white/[0.04] px-3 py-2 text-xs font-medium leading-5 text-[#c8cfca]">
          Source notebook: <span className="font-semibold text-[#f4f6f3]">{sourceTitle || "Source collection"}</span>
          <span className={`ml-2 inline-flex rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(sourceEvidenceStatus)}`}>
            {safeStatusLabel(sourceEvidenceStatus || "evidence_unknown")}
          </span>
        </div>
      )}

      {wikiPageTitle && (
        <div className="mb-3 rounded-lg border border-white/[0.08] bg-white/[0.04] px-3 py-2 text-xs font-medium text-[#c8cfca]">
          Active Wiki page: <span className="font-semibold text-[#f4f6f3]">{wikiPageTitle}</span>
        </div>
      )}

      {error && (
        <div className="mb-3 rounded-lg border border-rose-500/20 bg-rose-500/10 px-3 py-2 text-xs font-semibold leading-5 text-rose-200">
          {error}
        </div>
      )}

      {packs.length > 0 && (
        <div className="mb-3 flex gap-2 overflow-x-auto pb-1">
          {packs.map((pack) => (
            <button
              key={pack.id}
              type="button"
              onClick={() => {
                setSelected(pack);
                setSelectedArtifactId(pack.artifacts[0]?.id ?? null);
              }}
              className={`shrink-0 rounded-full border px-3 py-1 text-[11px] font-bold transition ${
                selected?.id === pack.id ? "border-[#6ed7ce]/45 bg-[#6ed7ce]/12 text-[#e8f6f4]" : "border-white/[0.08] bg-white/[0.04] text-[#9aa3a0]"
              }`}
            >
          {surfaceLabel(pack.packType)}
              {pack.sourceTitle ? ` - ${pack.sourceTitle}` : pack.wikiPageTitle ? ` - ${pack.wikiPageTitle}` : ""}
            </button>
          ))}
        </div>
      )}

      {selected ? (
        <div className="grid gap-3 lg:grid-cols-[minmax(0,0.92fr)_minmax(0,1.2fr)]">
          <div className="space-y-3">
            <div className="rounded-xl border border-white/[0.08] bg-white/[0.04] p-3">
              <div className="mb-2 flex flex-wrap items-center gap-2">
                <BookOpen className="h-4 w-4 text-[#6ed7ce]" />
                <h4 className="text-sm font-semibold text-[#f4f6f3]">{selected.title}</h4>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(selected.packStatus)}`}>
                  {safeStatusLabel(selected.packStatus)}
                </span>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(selected.evidenceStatus)}`}>
                  {safeStatusLabel(selected.evidenceStatus)}
                </span>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(selected.sourceReadiness)}`}>
                  Kaynak durumu: {safeStatusLabel(selected.sourceReadiness)}
                </span>
              </div>
              <p className="text-xs leading-5 text-[#667085]">{selected.summary}</p>
            </div>

            <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-1 xl:grid-cols-3">
              <SignalBox label="Tamamlanan" value={selected.completedConceptKeys.slice(0, 4).join(", ") || "Sinyal yok"} />
              <SignalBox label="Zayif alan" value={selected.weakConceptKeys.slice(0, 4).join(", ") || "Sinyal yok"} />
              <SignalBox label="Misconception" value={selected.misconceptionKeys.slice(0, 3).join(", ") || "Sinyal yok"} />
            </div>

            {selected.warnings.length > 0 && (
              <div className="rounded-xl border border-amber-500/20 bg-amber-500/10 px-3 py-2 text-xs font-semibold leading-5 text-[#8a6a33]">
                {selected.warnings.slice(0, 3).join(" - ")}
              </div>
            )}

            {selected.nextActions.length > 0 && (
              <div className="rounded-xl border border-[#526d82]/12 bg-white/45 p-3">
                <div className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-[0.16em] text-[#667085]">
                  <BrainCircuit className="h-3.5 w-3.5" />
                  Siradaki aksiyon
                </div>
                <div className="space-y-1.5">
                  {selected.nextActions.slice(0, 4).map((action) => (
                    <div key={`${action.actionType}-${action.userSafeLabel}`} className="rounded-lg bg-[#f7f9fa]/70 px-2 py-1.5 text-xs font-semibold text-[#344054]">
                      {action.userSafeLabel}
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div className="rounded-xl border border-[#526d82]/12 bg-white/45 p-3">
              <div className="mb-2 text-xs font-black uppercase tracking-[0.16em] text-[#667085]">Uretilecek ciktilar</div>
              <div className="flex flex-wrap gap-2">
                {artifactActions.map((type) => (
                  <button
                    key={type}
                    type="button"
                    onClick={() => buildArtifact(type)}
                    disabled={loading}
                    className="rounded-full border border-[#526d82]/14 bg-white/60 px-3 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
                  >
                    {labelFor(type)}
                  </button>
                ))}
              </div>
            </div>

            <div className="rounded-xl border border-[#526d82]/12 bg-white/45 p-3">
              <div className="mb-2 text-xs font-black uppercase tracking-[0.16em] text-[#667085]">Export package</div>
              <div className="mb-2 flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => selected && loadExportPreview(selected.id)}
                  disabled={loading}
                  className="rounded-full border border-[#526d82]/14 bg-white/60 px-3 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
                >
                  Preview
                </button>
                <button
                  type="button"
                  onClick={() => exportPack("markdown")}
                  disabled={loading}
                  className="rounded-full border border-[#526d82]/14 bg-white/60 px-3 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
                >
                  Markdown
                </button>
                <button
                  type="button"
                  onClick={() => exportPack("html")}
                  disabled={loading}
                  className="rounded-full border border-[#526d82]/14 bg-white/60 px-3 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
                >
                  Safe HTML
                </button>
                <button
                  type="button"
                  onClick={() => exportPack("manifest_only")}
                  disabled={loading}
                  className="rounded-full border border-[#526d82]/14 bg-white/60 px-3 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
                >
                  Manifest
                </button>
                <button
                  type="button"
                  onClick={() => exportPack("pptx_local_proof")}
                  disabled={loading}
                  className="rounded-full border border-amber-500/20 bg-amber-500/10 px-3 py-1 text-[11px] font-bold text-[#8a6a33] transition hover:text-[#172033] disabled:opacity-40"
                >
                  PPTX durumu
                </button>
              </div>
              <div className="grid gap-2">
                <MediaMeta label="Export readiness" value={safeStatusLabel(exportResult?.exportReadiness ?? exportPreview?.exportReadiness ?? "preview_pending")} tone={statusTone(exportResult?.exportReadiness ?? exportPreview?.exportReadiness)} />
                <MediaMeta label="PPTX" value={safeStatusLabel(exportResult?.pptxLocalProofAvailable ? "local_proof_available" : "pptx_not_enabled")} tone={statusTone("degraded")} />
                <MediaMeta label="Format" value={safeStatusLabel(exportResult?.format ?? "slide_preview")} />
                <MediaMeta label="Kaynak temeli" value={safeStatusLabel(exportResult?.sourceBasis ?? exportPreview?.sourceBasis ?? "evidence_insufficient")} tone={statusTone(exportResult?.sourceBasis ?? exportPreview?.sourceBasis)} />
                <MediaMeta label="Kaynak" value={safeStatusLabel(exportResult?.sourceReadiness ?? exportPreview?.sourceReadiness ?? selected.sourceReadiness)} tone={statusTone(exportResult?.sourceReadiness ?? exportPreview?.sourceReadiness ?? selected.sourceReadiness)} />
                <MediaMeta label="Erisilebilirlik" value={safeStatusLabel(exportResult?.accessibility?.status ?? (exportPreview ? "usable" : "preview_pending"))} />
                <MediaMeta label="Yüzey" value={surfaceLabel(exportResult?.surface ?? exportPreview?.surface ?? (surface === "source_notebook" ? "orkalm" : "wiki"))} />
                <MediaMeta label="Export scope" value={surfaceLabel(exportResult?.exportScope ?? exportPreview?.exportScope ?? "scoped_preview_pending")} />
                <MediaMeta label="Export scope key" value={String(exportResult?.exportScope ?? exportPreview?.exportScope ?? "scoped_preview_pending").replaceAll("_", " ").toLowerCase()} />
              </div>
              <div className="mt-2 rounded-lg border border-amber-500/20 bg-amber-500/10 px-2 py-1.5 text-[11px] font-semibold leading-5 text-[#8a6a33]">
                PPTX etkin degil; su an guvenli preview, Markdown, escaped HTML ve manifest paketi uretilir.
              </div>
              {exportPreview && (
                <div className="mt-2 rounded-lg border border-[#526d82]/10 bg-[#f7f9fa]/70 p-2 text-[11px] font-semibold leading-5 text-[#344054]">
                  <div>{exportPreview.slideCount} deck slides in preview. {exportPreview.accessibilitySummary}</div>
                  {exportPreview.warnings.length > 0 && (
                    <div className="mt-1 text-[#8a6a33]">
                      Kaynak uyari: {exportPreview.warnings.slice(0, 2).join(" - ")}
                    </div>
                  )}
                  {exportPreview.slides.length > 0 && (
                    <div className="mt-2 space-y-1">
                      <div className="text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Deck outline</div>
                      {exportPreview.slides.slice(0, 4).map((slide) => (
                        <div key={slide.slideId} className="rounded-md bg-white/70 px-2 py-1">
                          <div className="font-black text-[#172033]">
                            {slide.order}. {slide.title}
                          </div>
                          {slide.bullets[0] && <div className="text-[#667085]">{slide.bullets[0]}</div>}
                          <div className="mt-0.5 flex flex-wrap gap-1 text-[10px] text-[#667085]">
                            {slide.sourceLabel && <span>Kaynak: {slide.sourceLabel}</span>}
                            {slide.checkpointQuestion && <span>Checkpoint ready</span>}
                            {slide.hasSpeakerNotes && <span>Speaker notes ready</span>}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}
              {exportResult && (
                <div className="mt-2 rounded-lg border border-[#526d82]/10 bg-white/65 p-2">
                  <div className="mb-1 flex flex-wrap items-center gap-2 text-[11px] font-bold text-[#344054]">
                    Export package
                    <span className={`rounded-full border px-2 py-0.5 text-[10px] ${statusTone(exportResult.status)}`}>{exportResult.status}</span>
                    {exportResult.fileName && <span className="rounded-full bg-[#f7f9fa] px-2 py-0.5 text-[10px] text-[#667085]">{exportResult.fileName}</span>}
                  </div>
                  <pre className="max-h-36 overflow-auto whitespace-pre-wrap break-words rounded-md bg-[#172033] p-2 text-[10px] leading-4 text-white">
                    {exportResult.content.slice(0, 1800)}
                  </pre>
                </div>
              )}
            </div>
          </div>

          <div className="space-y-3">
            <div className="grid gap-2 md:grid-cols-2">
              {Object.entries(groupedArtifacts).map(([group, items]) => (
                <div key={group} className="rounded-xl border border-[#526d82]/12 bg-white/45 p-3">
                  <div className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-[0.16em] text-[#667085]">
                    {group === "Practice" || group === "Graph and metadata" ? <Map className="h-3.5 w-3.5" /> : group === "Media and export" ? <Layers className="h-3.5 w-3.5" /> : <FileText className="h-3.5 w-3.5" />}
                    {group}
                  </div>
                  {items.length === 0 ? (
                    <div className="text-[11px] font-semibold text-[#98a2b3]">No artifact yet.</div>
                  ) : (
                    <div className="space-y-1.5">
                      {items.map((artifact) => (
                        <button
                          key={artifact.id}
                          type="button"
                          onClick={() => setSelectedArtifactId(artifact.id)}
                          className={`w-full rounded-lg border px-2 py-1.5 text-left transition ${
                            selectedArtifact?.id === artifact.id ? "border-[#172033] bg-white" : "border-[#526d82]/10 bg-[#f7f9fa]/58"
                          }`}
                        >
                          <div className="flex flex-wrap items-center gap-1.5 text-[11px] font-bold text-[#344054]">
                            {labelFor(artifact.artifactType)}
                            <span className={`rounded-full border px-1.5 py-0.5 text-[9px] ${statusTone(artifact.sourceBasis)}`}>
                              {safeStatusLabel(artifact.sourceBasis)}
                            </span>
                          </div>
                          <div className="mt-0.5 truncate text-[10px] font-semibold text-[#667085]">{artifact.title}</div>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>

            {selectedArtifact && (
              <article className="rounded-xl border border-[#526d82]/12 bg-white/65 p-3">
                <div className="mb-2 flex flex-wrap items-center gap-2">
                  <h5 className="text-sm font-black text-[#172033]">{selectedArtifact.title}</h5>
                  <span className="rounded-full bg-[#dcecf3]/70 px-2 py-0.5 text-[10px] font-bold text-[#667085]">{labelFor(selectedArtifact.artifactType)}</span>
                  <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(selectedArtifact.sourceBasis)}`}>
                    {safeStatusLabel(selectedArtifact.sourceBasis)}
                  </span>
                </div>

                {selectedArtifact.artifactType.startsWith("audio") && (
                  <PhaseSevenAudioNotice artifact={selectedArtifact} />
                )}

                {isMediaExportArtifact(selectedArtifact) && (
                  <div className="mb-3 grid gap-2 sm:grid-cols-2">
                    <MediaMeta label="Durum" value={getArtifactStatus(selectedArtifact) ?? selectedArtifact.artifactStatus} tone={statusTone(getArtifactStatus(selectedArtifact) ?? selectedArtifact.artifactStatus)} />
                    <MediaMeta label="Export readiness" value={safeStatusLabel(getArtifactMetaValue(selectedArtifact, "exportReadiness") ?? "not_applicable")} tone={statusTone(getArtifactMetaValue(selectedArtifact, "exportReadiness"))} />
                    <MediaMeta label="Transcript" value={safeStatusLabel(getArtifactMetaValue(selectedArtifact, "transcriptAvailable") ?? getArtifactMetaValue(selectedArtifact, "transcriptArtifact") ?? "text_fallback")} />
                    <MediaMeta label="Kaynak" value={safeStatusLabel(getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis)} tone={statusTone(getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis)} />
                  </div>
                )}

                <ProfessionalArtifactPreview artifact={selectedArtifact} />

                <RichMarkdown
                  content={renderableContent(selectedArtifact)}
                  className="prose prose-sm max-w-none text-xs leading-5 prose-p:my-2 prose-li:my-0.5 prose-headings:text-[#172033] prose-p:text-[#344054] prose-li:text-[#344054]"
                />
              </article>
            )}
          </div>
        </div>
      ) : (
        <p className="text-sm text-[#667085]">
          {surface === "source_notebook"
            ? "No source artifact set exists yet. Build one after source evidence is available."
            : "No milestone artifact set exists yet. Build one after source, plan, practice, or learner context exists."}
        </p>
      )}
    </section>
  );
}
function SignalBox({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-white/[0.08] bg-white/[0.045] p-2">
      <div className="text-[10px] font-black uppercase tracking-[0.16em] text-[#87918d]">{label}</div>
      <div className="mt-1 break-words text-xs font-semibold text-[#eef2ee]">{value}</div>
    </div>
  );
}

function MediaMeta({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-[#f7f9fa]/70 px-2 py-1.5">
      <div className="text-[10px] font-black uppercase tracking-[0.16em] text-[#667085]">{label}</div>
      <div className={`mt-1 w-full rounded-md border px-2 py-1 text-[10px] font-bold leading-4 ${tone ?? "border-[#526d82]/14 bg-white/70 text-[#667085]"}`}>
        <span className="block whitespace-normal break-words">{value}</span>
      </div>
    </div>
  );
}

function ArtifactSurfaceSummary({ artifact }: { artifact: LearningArtifactDto }) {
  const data = parseArtifactJson(artifact);
  const surface = readString(data, "surface") ?? "surface_unknown";
  const contextType = readString(data, "contextType") ?? "context_unknown";
  const audioDeferred = readBoolean(data, "audioDeferred");
  const crossSurfaceSync = readBoolean(data, "crossSurfaceSync");

  return (
    <div className="mt-3 grid gap-2 sm:grid-cols-2">
      <MediaMeta label="Yüzey" value={surfaceLabel(surface)} tone={surface === "source_notebook" ? "border-sky-500/20 bg-sky-500/10 text-sky-700" : "border-emerald-500/20 bg-emerald-500/10 text-emerald-700"} />
      <MediaMeta label="Bağlam" value={surfaceLabel(contextType)} />
      <div className="sm:col-span-2 flex flex-wrap gap-1.5 rounded-lg border border-[#526d82]/12 bg-white/55 px-3 py-2 text-[11px] font-semibold leading-5 text-[#344054]">
        <StateChip label={crossSurfaceSync === false ? "Yüzeyler ayrı tutuluyor" : "Senkron durumu bilinmiyor"} tone={crossSurfaceSync === false ? "safe" : "watch"} />
        <StateChip label={audioDeferred === true ? "Sesli anlatım beklemede" : "Sesli anlatım hazır"} tone={audioDeferred === true ? "watch" : "safe"} />
      </div>
    </div>
  );
}

function ProfessionalArtifactPreview({ artifact }: { artifact: LearningArtifactDto }) {
  const data = parseArtifactJson(artifact);
  const surface = readString(data, "surface") ?? "surface_unknown";
  const contextType = readString(data, "contextType") ?? "context_unknown";
  const evidenceStatus = readString(data, "evidenceStatus") ?? artifact.sourceBasis;
  const sourceReadiness = readString(data, "sourceReadiness") ?? artifact.sourceBasis;
  const audioDeferred = readBoolean(data, "audioDeferred");
  const crossSurfaceSync = readBoolean(data, "crossSurfaceSync");
  const tags = readStringArray(data, "tags");
  const references = readStringArray(data, "references");
  const timelineItems = readStringArray(data, "timelineItems");
  const terms = readTerms(data);
  const slides = readSlides(data);
  const properties = readPropertyRows(data);
  const graphNodes = readGraphNodes(data);
  const graphEdges = readGraphEdges(data);
  const backlinks = readBacklinks(data);
  const linkedMentions = readLinkedMentions(data);
  const blockReferences = readReferenceRows(data);
  const templates = readTemplateRows(data);
  const searchFilters = readSearchFilters(data);
  const exportRows = readExportPreviewRows(data);
  const isGraphArtifact = ["graph_view", "backlink_map", "linked_mentions", "reference_map", "tag_map", "properties_panel", "template_set", "search_filter_index"].includes(artifact.artifactType);

  return (
    <div className="mb-3 space-y-2">
      <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
        <MediaMeta label="Detay yüzey" value={surfaceLabel(surface)} tone={surface === "source_notebook" ? "border-sky-500/20 bg-sky-500/10 text-sky-300" : "border-emerald-500/20 bg-emerald-500/10 text-emerald-200"} />
        <MediaMeta label="Detay bağlam" value={surfaceLabel(contextType)} />
        <MediaMeta label="Kanıt" value={safeStatusLabel(evidenceStatus)} tone={statusTone(evidenceStatus)} />
        <MediaMeta label="Kaynak durumu" value={safeStatusLabel(sourceReadiness)} tone={statusTone(sourceReadiness)} />
      </div>

      <div className="rounded-lg border border-[#526d82]/12 bg-[#f7f9fa]/70 px-3 py-2 text-[11px] font-semibold leading-5 text-[#344054]">
        <div className="flex flex-wrap gap-1.5">
          <StateChip label={crossSurfaceSync === false ? "Yüzeyler ayrı tutuluyor" : "Senkron durumu bilinmiyor"} tone={crossSurfaceSync === false ? "safe" : "watch"} />
          <StateChip label={audioDeferred === true ? "Sesli anlatım beklemede" : "Sesli anlatım hazır"} tone={audioDeferred === true ? "watch" : "safe"} />
          <StateChip label={surface === "source_notebook" ? "kaynak kapsamı" : "wiki sayfası kapsamı"} tone="neutral" />
        </div>
      </div>

      {tags.length > 0 && <ChipList title="Tags" items={tags} />}
      {terms.length > 0 && <TermGrid terms={terms} />}
      {artifact.artifactType === "properties_panel" && properties.length > 0 && <PropertiesMatrix rows={properties} />}
      {artifact.artifactType === "timeline" && <TimelinePreview terms={terms} timelineItems={timelineItems} />}
      {isGraphArtifact && <GraphScopePreview artifact={artifact} terms={terms} references={references} graphNodes={graphNodes} graphEdges={graphEdges} />}
      {artifact.artifactType === "backlink_map" && backlinks.length > 0 && <BacklinkPreview rows={backlinks} />}
      {artifact.artifactType === "linked_mentions" && linkedMentions.length > 0 && <LinkedMentionsPreview rows={linkedMentions} />}
      {artifact.artifactType === "reference_map" && blockReferences.length > 0 && <BlockReferencesPreview rows={blockReferences} />}
      {artifact.artifactType === "template_set" && templates.length > 0 && <TemplateSetPreview rows={templates} />}
      {artifact.artifactType === "search_filter_index" && searchFilters.length > 0 && <SearchFilterPreview rows={searchFilters} />}
      {exportRows.length > 0 && <ExportReadinessPreview rows={exportRows} />}
      {references.length > 0 && <ReferenceList references={references} />}
      {slides.length > 0 && <SlideDeckPreview slides={slides} />}
    </div>
  );
}

function PhaseSevenAudioNotice({ artifact }: { artifact: LearningArtifactDto }) {
  const jobId = getAudioJobId(artifact);
  const data = parseArtifactJson(artifact);
  const status = getArtifactStatus(artifact) ?? artifact.artifactStatus;
  const captionTrack = readString(data, "captionTrack");
  const classroomReady = readBoolean(data, "classroomReady");
  const fallbackReason = readString(data, "fallbackReason");
  return (
    <div className="mb-3 rounded-lg border border-emerald-500/20 bg-emerald-500/10 p-3 text-xs leading-5 text-emerald-200">
      <div className="font-black text-[#172033]">Audio overview is active.</div>
      <div className="mt-1 font-semibold">
        Transcript, caption fallback, and follow-up questions stay inside the same source context.
      </div>
      <div className="mt-2 flex flex-wrap gap-1.5">
        <StateChip label={status} tone={status === "ready" || status === "script_ready" || status === "caption_ready" ? "safe" : "watch"} />
        <StateChip label={classroomReady === false ? "study room pending" : "study room ready"} tone={classroomReady === false ? "watch" : "safe"} />
        <StateChip label={captionTrack ? "caption ready" : "caption fallback"} tone="neutral" />
      </div>
      {jobId && <div className="mt-1 text-[10px]">Audio overview is being prepared.</div>}
      {fallbackReason && <div className="mt-1 text-[11px] font-semibold">Fallback: {fallbackReason}</div>}
    </div>
  );
}

function StateChip({ label, tone }: { label: string; tone: "safe" | "watch" | "neutral" }) {
  const className = tone === "safe"
    ? "border-emerald-500/20 bg-emerald-500/10 text-emerald-200"
    : tone === "watch"
    ? "border-amber-500/20 bg-amber-500/10 text-amber-200"
    : "border-[#526d82]/14 bg-white/70 text-[#667085]";
  return <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${className}`}>{label}</span>;
}

function ChipList({ title, items }: { title: string; items: string[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-1 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">{title}</div>
      <div className="flex flex-wrap gap-1.5">
        {items.slice(0, 12).map((item) => (
          <span key={item} className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2 py-0.5 text-[10px] font-bold text-[#344054]">
            {item}
          </span>
        ))}
      </div>
    </div>
  );
}

function TermGrid({ terms }: { terms: ArtifactTerm[] }) {
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {terms.slice(0, 6).map((term) => (
        <div key={term.term} className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
          <div className="text-xs font-black text-[#172033]">{term.term}</div>
          {term.description && <div className="mt-1 text-[11px] font-semibold leading-5 text-[#667085]">{term.description}</div>}
        </div>
      ))}
    </div>
  );
}

function PropertiesMatrix({ rows }: { rows: ArtifactPropertyRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Property map</div>
      <div className="grid gap-1.5 sm:grid-cols-2">
        {rows.slice(0, 12).map((row) => (
          <div key={row.key} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1.5">
            <div className="text-[10px] font-black uppercase tracking-[0.12em] text-[#667085]">{displayArtifactText(row.label ?? row.key)}</div>
            <div className="mt-0.5 flex items-center justify-between gap-2">
              <span className="min-w-0 break-words text-[11px] font-semibold text-[#344054]">{displayArtifactText(row.value)}</span>
              {row.status && <span className="shrink-0 rounded-full border border-[#526d82]/14 bg-white/70 px-1.5 py-0.5 text-[9px] font-bold text-[#667085]">{safeStatusLabel(row.status)}</span>}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function TimelinePreview({ terms, timelineItems }: { terms: ArtifactTerm[]; timelineItems: string[] }) {
  const steps = timelineItems.length > 0
    ? timelineItems
    : terms.length > 0
    ? terms.slice(0, 5).map((term, index) => `${index + 1}. ${term.term}`)
    : ["1. Scope", "2. Study path", "3. Practice", "4. Diagram/deck", "5. Export"];
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Timeline path</div>
      <div className="space-y-1">
        {steps.map((step) => (
          <div key={step} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1 text-[11px] font-semibold text-[#344054]">
            {step}
          </div>
        ))}
      </div>
    </div>
  );
}

function GraphScopePreview({
  artifact,
  terms,
  references,
  graphNodes,
  graphEdges,
}: {
  artifact: LearningArtifactDto;
  terms: ArtifactTerm[];
  references: string[];
  graphNodes: ArtifactGraphNode[];
  graphEdges: ArtifactGraphEdge[];
}) {
  const data = parseArtifactJson(artifact);
  const root = readString(data, "title") ?? artifact.title;
  const surface = readString(data, "surface") ?? "surface_unknown";
  const contextType = readString(data, "contextType") ?? "context_unknown";
  const scopedNodeCount = graphNodes.length > 0 ? graphNodes.length : Math.max(1, terms.length + references.length + 2);
  const scopedEdgeCount = graphEdges.length > 0 ? graphEdges.length : Math.max(0, terms.length + references.length + 1);
  const crossSurfaceEdges = graphEdges.filter((edge) => edge.crossSurface === true).length;
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Graph scope</div>
      <div className="grid gap-2 sm:grid-cols-3">
        <SignalBox label="Root" value={root} />
        <SignalBox label="Nodes" value={`${scopedNodeCount} scoped`} />
        <SignalBox label="Links" value={`${scopedEdgeCount} / outside links ${crossSurfaceEdges}`} />
      </div>
      {graphNodes.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {graphNodes.slice(0, 8).map((node) => (
            <span key={node.id} className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2 py-0.5 text-[10px] font-bold text-[#344054]">
              {displayArtifactText(node.nodeType ?? "node")}: {displayArtifactText(node.label)}
            </span>
          ))}
        </div>
      )}
      <div className="mt-2 rounded-md border border-amber-500/20 bg-amber-500/10 px-2 py-1 text-[11px] font-semibold text-amber-200">
        {surfaceLabel(surface)} grafiği {surfaceLabel(contextType)} içinde kalır; dış çalışma alanlarına otomatik bağlantı yazılmaz.
      </div>
    </div>
  );
}

function BacklinkPreview({ rows }: { rows: ArtifactBacklinkRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Backlink scope</div>
      <div className="space-y-1">
        {rows.slice(0, 8).map((row) => (
          <div key={`${row.linkType}-${row.target}`} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1 text-[11px] font-semibold text-[#344054]">
            <span className="font-black">{displayArtifactText(row.linkType ?? "link")}</span>: {displayArtifactText(row.source)} -&gt; {displayArtifactText(row.target)}
          </div>
        ))}
      </div>
    </div>
  );
}

function LinkedMentionsPreview({ rows }: { rows: ArtifactMentionRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Linked mentions</div>
      <div className="flex flex-wrap gap-1.5">
        {rows.slice(0, 12).map((row) => (
          <span key={`${row.term}-${row.source}`} className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2 py-0.5 text-[10px] font-bold text-[#344054]">
            {displayArtifactText(row.term)} / {displayArtifactText(row.mentionScope ?? "scope")}
          </span>
        ))}
      </div>
    </div>
  );
}

function BlockReferencesPreview({ rows }: { rows: ArtifactReferenceRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Block/reference scope</div>
      <div className="space-y-1">
        {rows.slice(0, 8).map((row) => (
          <div key={`${row.refType}-${row.label}`} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1 text-[11px] font-semibold leading-5 text-[#344054]">
            <span className="font-black">{displayArtifactText(row.refType ?? "ref")}</span>: {displayArtifactText(row.label)}
          </div>
        ))}
      </div>
    </div>
  );
}

function TemplateSetPreview({ rows }: { rows: ArtifactTemplateRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Template set</div>
      <div className="grid gap-1.5 sm:grid-cols-2">
        {rows.slice(0, 10).map((row) => (
          <div key={row.templateKey} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1.5">
            <div className="text-xs font-black text-[#172033]">{displayArtifactText(row.title)}</div>
            <div className="mt-0.5 text-[10px] font-semibold text-[#667085]">{displayArtifactText(row.appliesTo)} / {labelFor(row.defaultArtifactType ?? "artifact")}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

function SearchFilterPreview({ rows }: { rows: ArtifactSearchFilterRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Search filters</div>
      <div className="grid gap-1.5 sm:grid-cols-2">
        {rows.slice(0, 12).map((row, index) => (
          <div key={`${row.filterKey}-${row.value}-${index}`} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1 text-[11px] font-semibold text-[#344054]">
            <span className="font-black">{displayArtifactText(row.label ?? row.filterKey)}</span>: {displayArtifactText(row.value)}
          </div>
        ))}
      </div>
    </div>
  );
}

function ExportReadinessPreview({ rows }: { rows: ArtifactExportPreviewRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Export readiness</div>
      <div className="flex flex-wrap gap-1.5">
        {rows.map((row) => (
          <span key={row.format} className="rounded-full border border-emerald-500/20 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-bold text-emerald-200">
            {displayArtifactText(row.label ?? row.format)}: {safeStatusLabel(row.status ?? "ready")}
          </span>
        ))}
      </div>
    </div>
  );
}

function ReferenceList({ references }: { references: string[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-1 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">References</div>
      <div className="space-y-1">
        {references.slice(0, 6).map((reference) => (
          <div key={reference} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1 text-[11px] font-semibold leading-5 text-[#344054]">
            {reference}
          </div>
        ))}
      </div>
    </div>
  );
}

function SlideDeckPreview({ slides }: { slides: SlidePreviewItem[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Deck preview</div>
      <div className="grid gap-2 xl:grid-cols-2">
        {slides.slice(0, 6).map((slide) => (
          <div key={`${slide.order ?? 0}-${slide.title}`} className="rounded-lg border border-[#526d82]/10 bg-[#f7f9fa]/70 p-2">
            <div className="text-xs font-black text-[#172033]">
              {slide.order ? `${slide.order}. ` : ""}{slide.title}
            </div>
            {slide.bullets && slide.bullets.length > 0 && (
              <ul className="mt-1 space-y-0.5 text-[11px] font-semibold leading-5 text-[#344054]">
                {slide.bullets.slice(0, 3).map((bullet) => <li key={bullet}>{bullet}</li>)}
              </ul>
            )}
            <div className="mt-2 flex flex-wrap gap-1">
              {slide.speakerNotes && <StateChip label="speaker notes" tone="safe" />}
              {slide.checkpointQuestion && <StateChip label="checkpoint" tone="safe" />}
              {slide.sourceLabel && <StateChip label={`source ${slide.sourceLabel}`} tone="neutral" />}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
