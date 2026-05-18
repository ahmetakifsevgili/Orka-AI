import { useEffect, useMemo, useState } from "react";
import {
  BookOpen,
  BrainCircuit,
  FileText,
  Headphones,
  Layers,
  Loader2,
  Map,
  RefreshCw,
  Sparkles,
} from "lucide-react";
import toast from "react-hot-toast";
import RichMarkdown from "./RichMarkdown";
import { AudioOverviewAPI, NotebookStudioAPI } from "@/services/api";
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
  "study_guide",
  "source_digest",
  "misconception_repair_pack",
  "audio_overview",
  "audio_transcript",
  "caption_track",
  "audio_script",
  "mind_map",
  "flashcard_set",
  "review_quiz",
  "slide_deck_outline",
  "video_ready_package",
  "slide_export_manifest",
  "narration_script",
  "visual_instruction_set",
  "media_accessibility_note",
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
  mind_map: "Mind map",
  flashcard_set: "Flashcard seti",
  review_quiz: "Review quiz",
  slide_deck_outline: "Slayt taslagi",
  video_ready_package: "Video-ready paket",
  slide_export_manifest: "Slide export manifest",
  narration_script: "Narration script",
  visual_instruction_set: "Visual instruction set",
  media_accessibility_note: "Medya erisilebilirlik",
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

const getAudioStatus = (artifact: LearningArtifactDto): string | null => {
  const value = parseArtifactJson(artifact).status;
  return typeof value === "string" && value.length > 0 ? value : null;
};

const getArtifactMetaValue = (artifact: LearningArtifactDto, key: string): string | null => {
  const value = parseArtifactJson(artifact)[key];
  return typeof value === "string" && value.length > 0 ? value : null;
};

const isMediaExportArtifact = (artifact: LearningArtifactDto) =>
  [
    "audio_overview",
    "audio_transcript",
    "caption_track",
    "video_ready_package",
    "slide_export_manifest",
    "narration_script",
    "visual_instruction_set",
    "media_accessibility_note",
  ].includes(artifact.artifactType);

const renderableContent = (artifact: LearningArtifactDto) => {
  if (artifact.renderFormat === "mermaid") {
    return `\`\`\`mermaid\n${artifact.safeContent}\n\`\`\``;
  }
  return artifact.safeContent;
};

const labelFor = (value: string) => artifactLabels[value] ?? value.replaceAll("_", " ");

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
      const result = await NotebookStudioAPI.listPacks(
        topicId,
        sessionId ?? undefined,
        isSourceSurface ? undefined : wikiPageId ?? undefined,
        isSourceSurface ? { surface: "source", sourceId } : {}
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
      const pack = surface === "source_notebook" && sourceId
        ? await NotebookStudioAPI.buildSourcePack(sourceId, {
            sessionId: sessionId ?? undefined,
            sourceId,
            sourceSurface: "source",
            packType: "source_digest",
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
      setExportPreview(preview);
    } catch {
      setExportPreview(null);
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
      "Sunum ve ses": [],
    };
    for (const artifact of artifacts) {
      if (["study_guide", "briefing_doc", "milestone_review"].includes(artifact.artifactType)) groups["Calisma"].push(artifact);
      else if (["source_digest", "misconception_repair_pack", "worked_example_set"].includes(artifact.artifactType)) groups["Kaynak ve onarim"].push(artifact);
      else if (["retrieval_card_set", "flashcard_set", "review_quiz", "mind_map"].includes(artifact.artifactType)) groups["Pratik"].push(artifact);
      else groups["Sunum ve ses"].push(artifact);
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
            {surface === "source_notebook" && sourceId ? "Source Pack" : wikiPageId ? "Sayfa Pack" : "Milestone Pack"}
          </button>
        </div>
      </div>

      {surface === "source_notebook" && sourceId && (
        <div className="mb-3 rounded-lg border border-[#526d82]/12 bg-white/55 px-3 py-2 text-xs font-semibold leading-5 text-[#344054]">
          OrkaLM kaynak defteri: <span className="font-black">{sourceTitle || "Secili kaynak"}</span>
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
                    {group === "Pratik" ? <Map className="h-3.5 w-3.5" /> : group === "Sunum ve ses" ? <Headphones className="h-3.5 w-3.5" /> : <FileText className="h-3.5 w-3.5" />}
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

                {selectedArtifact.artifactType === "audio_overview" && getAudioJobId(selectedArtifact) && (
                  <div className="mb-3 rounded-lg border border-[#526d82]/12 bg-[#f7f9fa]/70 p-2">
                    <div className="mb-1 flex items-center gap-1.5 text-[11px] font-bold text-[#344054]">
                      <Headphones className="h-3.5 w-3.5 text-[#47725d]" />
                      Audio overview
                      <span className="rounded-full bg-white/70 px-2 py-0.5 text-[10px] text-[#667085]">
                        {getAudioStatus(selectedArtifact) ?? "script-only"}
                      </span>
                    </div>
                    {getAudioStatus(selectedArtifact) === "ready" ? (
                      <audio controls src={AudioOverviewAPI.streamUrl(getAudioJobId(selectedArtifact)!)} className="w-full" />
                    ) : (
                      <div className="text-[11px] font-semibold leading-5 text-[#8a6a33]">
                        Ses dosyasi hazir degil; transcript ve tarayici TTS fallback kullanilabilir.
                      </div>
                    )}
                  </div>
                )}

                {isMediaExportArtifact(selectedArtifact) && (
                  <div className="mb-3 grid gap-2 sm:grid-cols-2">
                    <MediaMeta label="Durum" value={getAudioStatus(selectedArtifact) ?? selectedArtifact.artifactStatus} tone={statusTone(getAudioStatus(selectedArtifact) ?? selectedArtifact.artifactStatus)} />
                    <MediaMeta label="Export readiness" value={getArtifactMetaValue(selectedArtifact, "exportReadiness") ?? "not_applicable"} tone={statusTone(getArtifactMetaValue(selectedArtifact, "exportReadiness"))} />
                    <MediaMeta label="Transcript" value={getArtifactMetaValue(selectedArtifact, "transcriptAvailable") ?? getArtifactMetaValue(selectedArtifact, "transcriptArtifact") ?? "text_fallback"} />
                    <MediaMeta label="Kaynak" value={getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis} tone={statusTone(getArtifactMetaValue(selectedArtifact, "sourceReadiness") ?? selectedArtifact.sourceBasis)} />
                  </div>
                )}

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
