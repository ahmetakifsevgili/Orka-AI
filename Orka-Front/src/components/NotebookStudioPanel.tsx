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

const artifactLabels: Record<string, string> = {
  study_guide: "Calisma rehberi",
  briefing_doc: "Kisa briefing",
  source_digest: "Kaynak ozeti",
  misconception_repair_pack: "Onarim paketi",
  worked_example_set: "Ornek seti",
  retrieval_card_set: "Hatirlama kartlari",
  audio_overview: "Sesli anlatim",
  audio_script: "Audio transcript",
  audio_transcript: "Audio transcript",
  caption_track: "Caption track",
  glossary: "Glossary",
  timeline: "Timeline",
  mind_map: "Mind map",
  uml_diagram: "UML / Mermaid",
  flashcard_set: "Flashcard seti",
  review_quiz: "Review quiz",
  slide_deck_outline: "Slayt taslagi",
  video_ready_package: "Video-ready paket",
  slide_export_manifest: "Slide export manifest",
  narration_script: "Narration script",
  visual_instruction_set: "Visual instruction set",
  media_accessibility_note: "Medya erisilebilirlik",
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
      return "border-emerald-500/20 bg-emerald-500/8 text-[#47725d]";
    case "stale":
    case "degraded":
    case "evidence_insufficient":
      return "border-amber-500/24 bg-amber-500/10 text-[#8a6a33]";
    default:
      return "border-[#526d82]/14 bg-[#f7f9fa]/70 text-[#667085]";
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
      setError("Notebook Studio paketleri yuklenemedi. Daha sonra tekrar deneyebilirsin.");
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
      toast.success("Notebook Studio paketi hazirlandi.");
    } catch {
      setError("Notebook Studio paketi hazirlanamadi. Kaynak veya Wiki sayfasi durumu sinirli olabilir.");
      toast.error("Notebook Studio paketi hazirlanamadi.");
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
      setError("Notebook artifact uretilemedi. Guvenlik, kaynak veya pack durumu nedeniyle degrade olmus olabilir.");
      toast.error("Notebook artifact uretilemedi.");
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
      "Calisma": [],
      "Kaynak ve onarim": [],
      "Pratik": [],
      "Graph ve metadata": [],
      "Sunum ve export": [],
    };
    for (const artifact of artifacts) {
      if (["study_guide", "briefing_doc", "milestone_review", "glossary", "timeline"].includes(artifact.artifactType)) groups["Calisma"].push(artifact);
      else if (["source_digest", "misconception_repair_pack", "worked_example_set"].includes(artifact.artifactType)) groups["Kaynak ve onarim"].push(artifact);
      else if (["retrieval_card_set", "flashcard_set", "review_quiz"].includes(artifact.artifactType)) groups["Pratik"].push(artifact);
      else if (["mind_map", "uml_diagram", "properties_panel", "tag_map", "backlink_map", "linked_mentions", "reference_map", "graph_view", "template_set", "search_filter_index"].includes(artifact.artifactType)) groups["Graph ve metadata"].push(artifact);
      else if (["audio_script", "audio_transcript", "caption_track", "narration_script", "audio_overview"].includes(artifact.artifactType)) groups["Sunum ve export"].push(artifact);
      else groups["Sunum ve export"].push(artifact);
    }
    return groups;
  }, [artifacts]);

  return (
    <section className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/58 p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Layers className="h-4 w-4 text-[#47725d]" />
          <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">Notebook Studio</span>
          {loading && <Loader2 className="h-3.5 w-3.5 animate-spin text-[#98a2b3]" />}
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={refresh}
            disabled={loading}
            className="rounded-lg border border-[#526d82]/14 bg-white/60 p-1.5 text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
            aria-label="Notebook paketlerini yenile"
          >
            <RefreshCw className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={buildPack}
            disabled={loading}
            className="inline-flex items-center gap-1.5 rounded-lg bg-[#172033] px-3 py-1.5 text-xs font-bold text-white transition hover:bg-[#2a3447] disabled:opacity-40"
          >
            <Sparkles className="h-3.5 w-3.5" />
            {surface === "source_notebook" ? (sourceId ? "Source Pack" : "OrkaLM Pack") : wikiPageId ? "Sayfa Pack" : "Milestone Pack"}
          </button>
        </div>
      </div>

      {surface === "source_notebook" && (
        <div className="mb-3 rounded-lg border border-[#526d82]/12 bg-white/55 px-3 py-2 text-xs font-semibold leading-5 text-[#344054]">
          OrkaLM kaynak defteri: <span className="font-black">{sourceTitle || "Kaynak koleksiyonu"}</span>
          <span className={`ml-2 inline-flex rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(sourceEvidenceStatus)}`}>
            {sourceEvidenceStatus || "evidence_unknown"}
          </span>
        </div>
      )}

      {wikiPageTitle && (
        <div className="mb-3 rounded-lg border border-[#526d82]/12 bg-white/55 px-3 py-2 text-xs font-semibold text-[#344054]">
          Aktif Wiki sayfasi: <span className="font-black">{wikiPageTitle}</span>
        </div>
      )}

      {error && (
        <div className="mb-3 rounded-lg border border-rose-500/18 bg-rose-500/8 px-3 py-2 text-xs font-semibold leading-5 text-[#9f3a46]">
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
                selected?.id === pack.id ? "border-[#172033] bg-white text-[#172033]" : "border-[#526d82]/14 bg-white/50 text-[#667085]"
              }`}
            >
          {pack.packType.replaceAll("_", " ")}
              {pack.sourceTitle ? ` - ${pack.sourceTitle}` : pack.wikiPageTitle ? ` - ${pack.wikiPageTitle}` : ""}
            </button>
          ))}
        </div>
      )}

      {selected ? (
        <div className="grid gap-3 lg:grid-cols-[minmax(0,0.92fr)_minmax(0,1.2fr)]">
          <div className="space-y-3">
            <div className="rounded-xl border border-[#526d82]/12 bg-white/55 p-3">
              <div className="mb-2 flex flex-wrap items-center gap-2">
                <BookOpen className="h-4 w-4 text-[#47725d]" />
                <h4 className="text-sm font-black text-[#172033]">{selected.title}</h4>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(selected.packStatus)}`}>
                  {selected.packStatus}
                </span>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(selected.evidenceStatus)}`}>
                  {selected.evidenceStatus}
                </span>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusTone(selected.sourceReadiness)}`}>
                  Kaynak hazirlik: {selected.sourceReadiness}
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
              <div className="rounded-xl border border-amber-500/18 bg-amber-500/8 px-3 py-2 text-xs font-semibold leading-5 text-[#8a6a33]">
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
              <div className="mb-2 text-xs font-black uppercase tracking-[0.16em] text-[#667085]">Slide export paketi</div>
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
                  className="rounded-full border border-amber-500/20 bg-amber-500/8 px-3 py-1 text-[11px] font-bold text-[#8a6a33] transition hover:text-[#172033] disabled:opacity-40"
                >
                  PPTX durumu
                </button>
              </div>
              <div className="grid gap-2 sm:grid-cols-2">
                <MediaMeta label="Export readiness" value={exportResult?.exportReadiness ?? exportPreview?.exportReadiness ?? "preview_pending"} tone={statusTone(exportResult?.exportReadiness ?? exportPreview?.exportReadiness)} />
                <MediaMeta label="PPTX" value={exportResult?.pptxLocalProofAvailable ? "local_proof_available" : "pptx_not_enabled"} tone={statusTone("degraded")} />
                <MediaMeta label="Format" value={exportResult?.format ?? "slide_preview"} />
                <MediaMeta label="Kaynak temeli" value={exportResult?.sourceBasis ?? exportPreview?.sourceBasis ?? "evidence_insufficient"} tone={statusTone(exportResult?.sourceBasis ?? exportPreview?.sourceBasis)} />
                <MediaMeta label="Kaynak" value={exportResult?.sourceReadiness ?? exportPreview?.sourceReadiness ?? selected.sourceReadiness} tone={statusTone(exportResult?.sourceReadiness ?? exportPreview?.sourceReadiness ?? selected.sourceReadiness)} />
                <MediaMeta label="Erisilebilirlik" value={exportResult?.accessibility?.status ?? (exportPreview ? "usable" : "preview_pending")} />
                <MediaMeta label="Surface" value={exportResult?.surface ?? exportPreview?.surface ?? (surface === "source_notebook" ? "orkalm" : "wiki")} />
                <MediaMeta label="Export scope" value={exportResult?.exportScope ?? exportPreview?.exportScope ?? "scoped_preview_pending"} />
              </div>
              <div className="mt-2 rounded-lg border border-amber-500/16 bg-amber-500/8 px-2 py-1.5 text-[11px] font-semibold leading-5 text-[#8a6a33]">
                PPTX etkin degil; su an guvenli preview, Markdown, escaped HTML ve manifest paketi uretilir.
              </div>
              {exportPreview && (
                <div className="mt-2 rounded-lg border border-[#526d82]/10 bg-[#f7f9fa]/70 p-2 text-[11px] font-semibold leading-5 text-[#344054]">
                  <div>{exportPreview.slideCount} slayt preview hazir. {exportPreview.accessibilitySummary}</div>
                  {exportPreview.warnings.length > 0 && (
                    <div className="mt-1 text-[#8a6a33]">
                      Kaynak uyari: {exportPreview.warnings.slice(0, 2).join(" - ")}
                    </div>
                  )}
                  {exportPreview.slides.length > 0 && (
                    <div className="mt-2 space-y-1">
                      <div className="text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Slayt listesi</div>
                      {exportPreview.slides.slice(0, 4).map((slide) => (
                        <div key={slide.slideId} className="rounded-md bg-white/70 px-2 py-1">
                          <div className="font-black text-[#172033]">
                            {slide.order}. {slide.title}
                          </div>
                          {slide.bullets[0] && <div className="text-[#667085]">{slide.bullets[0]}</div>}
                          <div className="mt-0.5 flex flex-wrap gap-1 text-[10px] text-[#667085]">
                            {slide.sourceLabel && <span>Kaynak: {slide.sourceLabel}</span>}
                            {slide.checkpointQuestion && <span>Checkpoint var</span>}
                            {slide.hasSpeakerNotes && <span>Speaker notes var</span>}
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
                    {group === "Pratik" || group === "Graph ve metadata" ? <Map className="h-3.5 w-3.5" /> : group === "Sunum ve export" ? <Layers className="h-3.5 w-3.5" /> : <FileText className="h-3.5 w-3.5" />}
                    {group}
                  </div>
                  {items.length === 0 ? (
                    <div className="text-[11px] font-semibold text-[#98a2b3]">Henuz cikti yok.</div>
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
                              {artifact.sourceBasis}
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
                    {selectedArtifact.sourceBasis}
                  </span>
                </div>

                {selectedArtifact.artifactType.startsWith("audio") && (
                  <PhaseSevenAudioNotice artifact={selectedArtifact} />
                )}

                {isMediaExportArtifact(selectedArtifact) && (
                  <div className="mb-3 grid gap-2 sm:grid-cols-2">
                    <MediaMeta label="Durum" value={getArtifactStatus(selectedArtifact) ?? selectedArtifact.artifactStatus} tone={statusTone(getArtifactStatus(selectedArtifact) ?? selectedArtifact.artifactStatus)} />
                    <MediaMeta label="Export readiness" value={getArtifactMetaValue(selectedArtifact, "exportReadiness") ?? "not_applicable"} tone={statusTone(getArtifactMetaValue(selectedArtifact, "exportReadiness"))} />
                    <MediaMeta label="Transcript" value={getArtifactMetaValue(selectedArtifact, "transcriptAvailable") ?? getArtifactMetaValue(selectedArtifact, "transcriptArtifact") ?? "text_fallback"} />
                    <MediaMeta label="Kaynak" value={getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis} tone={statusTone(getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis)} />
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
            ? "Bu kaynak icin henuz OrkaLM source pack yok. Source Pack ile kaynak, citation durumu ve calisma ciktilarini tek yerde toplayabilirsin."
            : "Bu konu icin henuz Notebook Studio paketi yok. Milestone Pack ile kaynak, plan, quiz ve ogrenci durumunu tek calisma paketinde toplayabilirsin."}
        </p>
      )}
    </section>
  );
}

function SignalBox({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/45 p-2">
      <div className="text-[10px] font-black uppercase tracking-[0.16em] text-[#667085]">{label}</div>
      <div className="mt-1 break-words text-xs font-semibold text-[#344054]">{value}</div>
    </div>
  );
}

function MediaMeta({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-[#f7f9fa]/70 px-2 py-1.5">
      <div className="text-[10px] font-black uppercase tracking-[0.16em] text-[#667085]">{label}</div>
      <div className={`mt-1 inline-flex max-w-full rounded-full border px-2 py-0.5 text-[10px] font-bold ${tone ?? "border-[#526d82]/14 bg-white/70 text-[#667085]"}`}>
        <span className="truncate">{value}</span>
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
        <MediaMeta label="Surface" value={surface} tone={surface === "orkalm" ? "border-sky-500/20 bg-sky-500/8 text-sky-700" : "border-emerald-500/20 bg-emerald-500/8 text-[#47725d]"} />
        <MediaMeta label="Context" value={contextType} />
        <MediaMeta label="Evidence" value={evidenceStatus} tone={statusTone(evidenceStatus)} />
        <MediaMeta label="Source readiness" value={sourceReadiness} tone={statusTone(sourceReadiness)} />
      </div>

      <div className="rounded-lg border border-[#526d82]/12 bg-[#f7f9fa]/70 px-3 py-2 text-[11px] font-semibold leading-5 text-[#344054]">
        <div className="flex flex-wrap gap-1.5">
          <StateChip label={crossSurfaceSync === false ? "cross-surface sync kapali" : "sync durumu bilinmiyor"} tone={crossSurfaceSync === false ? "safe" : "watch"} />
          <StateChip label={audioDeferred === true ? "legacy audio deferred" : "phase 7 audio active"} tone={audioDeferred === true ? "watch" : "safe"} />
          <StateChip label={surface === "orkalm" ? "source notebook scope" : "wiki lesson scope"} tone="neutral" />
        </div>
      </div>

      {tags.length > 0 && <ChipList title="Tags" items={tags} />}
      {terms.length > 0 && <TermGrid terms={terms} />}
      {artifact.artifactType === "properties_panel" && properties.length > 0 && <PropertiesMatrix rows={properties} />}
      {artifact.artifactType === "timeline" && <TimelinePreview terms={terms} timelineItems={timelineItems} />}
      {isGraphArtifact && <GraphContractPreview artifact={artifact} terms={terms} references={references} graphNodes={graphNodes} graphEdges={graphEdges} />}
      {artifact.artifactType === "backlink_map" && backlinks.length > 0 && <BacklinkPreview rows={backlinks} />}
      {artifact.artifactType === "linked_mentions" && linkedMentions.length > 0 && <LinkedMentionsPreview rows={linkedMentions} />}
      {artifact.artifactType === "reference_map" && blockReferences.length > 0 && <BlockReferencesPreview rows={blockReferences} />}
      {artifact.artifactType === "template_set" && templates.length > 0 && <TemplateSetPreview rows={templates} />}
      {artifact.artifactType === "search_filter_index" && searchFilters.length > 0 && <SearchFilterPreview rows={searchFilters} />}
      {exportRows.length > 0 && <ExportContractPreview rows={exportRows} />}
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
    <div className="mb-3 rounded-lg border border-emerald-500/18 bg-emerald-500/8 p-3 text-xs leading-5 text-[#47725d]">
      <div className="font-black text-[#172033]">Sesli ders paketi aktif.</div>
      <div className="mt-1 font-semibold">
        Transcript, caption fallback ve sesli calisma odasi sorusu ayni surface context'inde tutulur; cross-surface sync kapalidir.
      </div>
      <div className="mt-2 flex flex-wrap gap-1.5">
        <StateChip label={status} tone={status === "ready" || status === "script_ready" || status === "caption_ready" ? "safe" : "watch"} />
        <StateChip label={classroomReady === false ? "study room pending" : "study room ready"} tone={classroomReady === false ? "watch" : "safe"} />
        <StateChip label={captionTrack ? "caption ready" : "caption fallback"} tone="neutral" />
      </div>
      {jobId && <div className="mt-1 font-mono text-[10px]">audio job: {jobId}</div>}
      {fallbackReason && <div className="mt-1 text-[11px] font-semibold">Fallback: {fallbackReason}</div>}
    </div>
  );
}

function StateChip({ label, tone }: { label: string; tone: "safe" | "watch" | "neutral" }) {
  const className = tone === "safe"
    ? "border-emerald-500/20 bg-emerald-500/8 text-[#47725d]"
    : tone === "watch"
    ? "border-amber-500/20 bg-amber-500/8 text-[#8a6a33]"
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
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Properties contract</div>
      <div className="grid gap-1.5 sm:grid-cols-2">
        {rows.slice(0, 12).map((row) => (
          <div key={row.key} className="rounded-md bg-[#f7f9fa]/70 px-2 py-1.5">
            <div className="text-[10px] font-black uppercase tracking-[0.12em] text-[#667085]">{row.label ?? row.key}</div>
            <div className="mt-0.5 flex items-center justify-between gap-2">
              <span className="min-w-0 break-words text-[11px] font-semibold text-[#344054]">{row.value}</span>
              {row.status && <span className="shrink-0 rounded-full border border-[#526d82]/14 bg-white/70 px-1.5 py-0.5 text-[9px] font-bold text-[#667085]">{row.status}</span>}
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
    : ["1. Context capture", "2. Study path", "3. Practice", "4. Slide/diagram", "5. Export"];
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

function GraphContractPreview({
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
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Scoped graph contract</div>
      <div className="grid gap-2 sm:grid-cols-3">
        <SignalBox label="Root" value={root} />
        <SignalBox label="Nodes" value={`${scopedNodeCount} scoped`} />
        <SignalBox label="Edges" value={`${scopedEdgeCount} / cross-surface ${crossSurfaceEdges}`} />
      </div>
      {graphNodes.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {graphNodes.slice(0, 8).map((node) => (
            <span key={node.id} className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2 py-0.5 text-[10px] font-bold text-[#344054]">
              {node.nodeType ?? "node"}: {node.label}
            </span>
          ))}
        </div>
      )}
      <div className="mt-2 rounded-md border border-amber-500/16 bg-amber-500/8 px-2 py-1 text-[11px] font-semibold text-[#8a6a33]">
        {surface} graph, {contextType} icinde kalir; otomatik cross-surface edge yazilmaz.
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
            <span className="font-black">{row.linkType ?? "link"}</span>: {row.source} -&gt; {row.target}
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
            {row.term} / {row.mentionScope ?? "scope"}
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
            <span className="font-black">{row.refType ?? "ref"}</span>: {row.label}
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
            <div className="text-xs font-black text-[#172033]">{row.title}</div>
            <div className="mt-0.5 text-[10px] font-semibold text-[#667085]">{row.appliesTo} / {row.defaultArtifactType}</div>
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
            <span className="font-black">{row.label ?? row.filterKey}</span>: {row.value}
          </div>
        ))}
      </div>
    </div>
  );
}

function ExportContractPreview({ rows }: { rows: ArtifactExportPreviewRow[] }) {
  return (
    <div className="rounded-lg border border-[#526d82]/12 bg-white/55 p-2">
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Export readiness</div>
      <div className="flex flex-wrap gap-1.5">
        {rows.map((row) => (
          <span key={row.format} className="rounded-full border border-emerald-500/20 bg-emerald-500/8 px-2 py-0.5 text-[10px] font-bold text-[#47725d]">
            {row.label ?? row.format}: {row.status ?? "ready"}
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
      <div className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Slide preview</div>
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
