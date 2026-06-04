import { expect, test, type Page, type Route } from "@playwright/test";

const topicId = "topic-contract-1";
const sourceId = "source-contract-1";
const wikiPageId = "wiki-page-1";
const packId = "pack-contract-1";
const wikiPackId = "pack-wiki-contract-1";
const orkalmAudioJobId = "audio-job-orkalm-contract";
const wikiAudioJobId = "audio-job-wiki-contract";
const classroomSessionId = "classroom-session-contract";
const now = "2026-06-03T09:00:00.000Z";
const phaseScope = [
  "phase_1_contract",
  "phase_2_graph_metadata",
  "phase_3_text_notebook",
  "phase_4_slide_diagram",
  "phase_5_search_template_export",
  "phase_6_internal_connections",
  "phase_7_audio_classroom",
];

const user = {
  id: "user-contract-1",
  firstName: "Contract",
  lastName: "Tester",
  email: "contract@orka.local",
};

type SurfaceMode = "orkalm" | "wiki";

function baseArtifact(overrides: Record<string, unknown>) {
  return {
    id: "artifact-base",
    topicId,
    sessionId: null,
    tutorTurnStateId: null,
    tutorActionTraceId: null,
    teachingArtifactId: null,
    activeLessonSnapshotId: null,
    studentContextSnapshotId: null,
    planQualitySnapshotId: null,
    assessmentQualitySnapshotId: null,
    sourceEvidenceBundleId: null,
    wikiNotebookSectionKey: null,
    conceptKey: null,
    conceptLabel: null,
    artifactType: "briefing_doc",
    artifactStatus: "ready",
    origin: "notebook_studio",
    renderFormat: "markdown",
    title: "Contract artifact",
    safeContent: "Contract artifact content.",
    contentJson: null,
    sourceBasis: "source_grounded",
    citationIds: [],
    toolTraceIds: [],
    phaseScope,
    accessibility: {
      status: "usable",
      altText: null,
      caption: null,
      summary: null,
      textFallback: null,
      language: "tr",
      issues: [],
    },
    safety: {
      status: "safe",
      warnings: [],
      blockingIssues: [],
    },
    createdAt: now,
    updatedAt: now,
    ...overrides,
  };
}

function artifactJson(extra: Record<string, unknown>) {
  return JSON.stringify({
    surface: "orkalm",
    contextType: "source_notebook",
    packType: "source_digest",
    sourceId,
    wikiPageId: null,
    sourceSurface: "source",
    evidenceStatus: "source_grounded",
    sourceReadiness: "source_grounded",
    phaseScope,
    audioDeferred: false,
    audioPhase: "phase_7_active",
    crossSurfaceSync: false,
    tags: ["source", "contract", "phase-2-7"],
    references: ["contract.pdf p.2", "SourceChunk:chunk-1"],
    terms: [
      { term: "Citation", description: "Kaynak chunk ile desteklenen iz." },
      { term: "Notebook scope", description: "OrkaLM icinde kalan kaynak defteri." },
    ],
    properties: [
      { key: "surface", label: "Surface", value: "orkalm", status: "locked" },
      { key: "context_type", label: "Context type", value: "source_notebook", status: "locked" },
      { key: "source_upload", label: "Source upload", value: "enabled_in_orkalm", status: "enabled" },
      { key: "cross_surface_sync", label: "Cross-surface sync", value: "disabled", status: "locked" },
    ],
    timelineItems: [
      "Phase 1 contract locks orkalm / source_notebook.",
      "Phase 2 graph and metadata stay source scoped.",
      "Phase 3 text artifacts use source citations.",
      "Phase 4 slides and diagrams remain export-safe.",
      "Phase 7 audio, captions, fallback and classroom are active.",
    ],
    graphNodes: [
      { id: "orkalm:root", label: "OrkaLM source notebook", nodeType: "root", surface: "orkalm", status: "active" },
      { id: "orkalm:context", label: "source_notebook", nodeType: "context", surface: "orkalm", status: "locked" },
      { id: "orkalm:ref:1", label: "contract.pdf p.2", nodeType: "citation", surface: "orkalm", status: "active" },
    ],
    graphEdges: [
      { sourceId: "orkalm:root", targetId: "orkalm:context", edgeType: "uses_context", scope: "source_notebook", crossSurface: false },
      { sourceId: "orkalm:root", targetId: "orkalm:ref:1", edgeType: "cites", scope: "source_notebook", crossSurface: false },
    ],
    backlinks: [
      { source: "OrkaLM source notebook", target: "citations", linkType: "citation_backlink", surface: "orkalm", status: "scoped" },
      { source: "OrkaLM source notebook", target: "cross-surface graph", linkType: "cross_surface_edge", surface: "orkalm", status: "disabled" },
    ],
    linkedMentions: [
      { term: "Citation", mentionScope: "source_notebook", source: "source_notebook", status: "scoped" },
    ],
    blockReferences: [
      { refType: "source_chunk_ref", label: "contract.pdf p.2", scope: "orkalm", status: "scoped" },
    ],
    templates: [
      { templateKey: "briefing", title: "Briefing / quick summary", appliesTo: "source_notebook", defaultArtifactType: "briefing_doc" },
      { templateKey: "slides", title: "Slide outline with notes", appliesTo: "source_notebook", defaultArtifactType: "slide_deck_outline" },
    ],
    searchFilters: [
      { filterKey: "surface", label: "Surface", value: "orkalm", surface: "orkalm" },
      { filterKey: "context_type", label: "Context type", value: "source_notebook", surface: "orkalm" },
    ],
    exportPreview: [
      { format: "markdown", label: "Markdown preview", status: "ready" },
      { format: "manifest", label: "Artifact manifest", status: "ready" },
    ],
    ...extra,
  });
}

function wikiArtifactJson(extra: Record<string, unknown>) {
  return JSON.stringify({
    surface: "wiki",
    contextType: "wiki_page",
    packType: "wiki_page_review",
    wikiPageId: "wiki-page-1",
    sourceId: null,
    sourceSurface: null,
    evidenceStatus: "wiki_grounded",
    sourceReadiness: "wiki_grounded",
    phaseScope,
    audioDeferred: false,
    audioPhase: "phase_7_active",
    crossSurfaceSync: false,
    tags: ["wiki", "contract", "phase-2-7"],
    references: ["WikiBlock:block-1"],
    terms: [
      { term: "Wiki lesson scope", description: "Normal ders akisi icindeki Wiki sayfasi." },
    ],
    properties: [
      { key: "surface", label: "Surface", value: "wiki", status: "locked" },
      { key: "context_type", label: "Context type", value: "wiki_page", status: "locked" },
      { key: "source_upload", label: "Source upload", value: "hidden_in_wiki", status: "hidden" },
      { key: "cross_surface_sync", label: "Cross-surface sync", value: "disabled", status: "locked" },
    ],
    graphNodes: [
      { id: "wiki:root", label: "Wiki lesson", nodeType: "root", surface: "wiki", status: "active" },
      { id: "wiki:context", label: "wiki_page", nodeType: "context", surface: "wiki", status: "locked" },
    ],
    graphEdges: [
      { sourceId: "wiki:root", targetId: "wiki:context", edgeType: "uses_context", scope: "wiki_page", crossSurface: false },
    ],
    backlinks: [
      { source: "Wiki lesson", target: "wiki page blocks", linkType: "wiki_block_backlink", surface: "wiki", status: "scoped" },
    ],
    linkedMentions: [
      { term: "Wiki lesson scope", mentionScope: "wiki_page", source: "wiki_page", status: "scoped" },
    ],
    blockReferences: [
      { refType: "wiki_block_ref", label: "WikiBlock:block-1", scope: "wiki", status: "scoped" },
    ],
    templates: [
      { templateKey: "briefing", title: "Briefing / quick summary", appliesTo: "wiki_page", defaultArtifactType: "briefing_doc" },
    ],
    searchFilters: [
      { filterKey: "surface", label: "Surface", value: "wiki", surface: "wiki" },
      { filterKey: "context_type", label: "Context type", value: "wiki_page", surface: "wiki" },
    ],
    exportPreview: [
      { format: "markdown", label: "Markdown preview", status: "ready" },
    ],
    ...extra,
  });
}

const slideArtifact = baseArtifact({
  id: "artifact-slide",
  artifactType: "slide_deck_outline",
  title: "OrkaLM slide outline",
  safeContent: "## OrkaLM slide outline\n\n- Source-grounded slide draft",
  contentJson: artifactJson({
    title: "OrkaLM slide outline",
    slides: [
      {
        order: 1,
        title: "Kaynak defteri akisi",
        bullets: ["Upload OrkaLM icinde kalir", "Wiki context'e otomatik yazilmaz"],
        speakerNotes: "Kaynak kanitini once citation olarak goster.",
        sourceLabel: "contract.pdf p.2",
        checkpointQuestion: "Bu slayt Wiki state'e yaziyor mu?",
        visualSuggestion: "Two separated lanes",
      },
    ],
  }),
});

const propertiesArtifact = baseArtifact({
  id: "artifact-properties",
  artifactType: "properties_panel",
  title: "OrkaLM properties and graph contract",
  safeContent: "Properties, tags, backlinks and graph stay scoped to OrkaLM.",
  contentJson: artifactJson({
    title: "OrkaLM properties and graph contract",
  }),
});

const umlArtifact = baseArtifact({
  id: "artifact-uml",
  artifactType: "uml_diagram",
  title: "Source notebook UML",
  renderFormat: "mermaid",
  safeContent: "classDiagram\n  class OrkaLM\n  class SourceNotebook\n  OrkaLM --> SourceNotebook",
  contentJson: artifactJson({
    title: "Source notebook UML",
    diagramType: "classDiagram",
  }),
});

const activeAudioArtifact = baseArtifact({
  id: "artifact-audio",
  artifactType: "audio_overview",
  artifactStatus: "ready",
  title: "Active audio overview",
  safeContent: "[HOCA]: Source notebook audio is active.\n[ASISTAN]: Caption fallback and classroom question flow are ready.",
  contentJson: artifactJson({
    title: "Active audio overview",
    status: "ready",
    audioOverviewJobId: "audio-job-1",
    audioPhase: "phase_7_active",
    audioDeferred: false,
    classroomReady: true,
    captionTrack: "WEBVTT\n\n1\n00:00:00.000 --> 00:00:12.000\nHOCA: Source notebook audio is active.",
    transcriptArtifact: true,
  }),
});

const wikiStudyGuideArtifact = baseArtifact({
  id: "artifact-wiki-study-guide",
  topicId,
  artifactType: "study_guide",
  title: "Wiki Study Guide",
  safeContent: "Wiki study guide stays in lesson scope.",
  sourceBasis: "wiki_grounded",
  contentJson: wikiArtifactJson({
    title: "Wiki Study Guide",
  }),
});

const wikiPropertiesArtifact = baseArtifact({
  id: "artifact-wiki-properties",
  topicId,
  artifactType: "properties_panel",
  title: "Wiki properties and graph contract",
  safeContent: "Properties, tags, backlinks and graph stay scoped to Wiki.",
  sourceBasis: "wiki_grounded",
  contentJson: wikiArtifactJson({
    title: "Wiki properties and graph contract",
  }),
});

const wikiSlideArtifact = baseArtifact({
  id: "artifact-wiki-slide",
  topicId,
  artifactType: "slide_deck_outline",
  title: "Wiki slide outline",
  safeContent: "## Wiki slide outline\n\n- Lesson-scoped slide draft",
  sourceBasis: "wiki_grounded",
  contentJson: wikiArtifactJson({
    title: "Wiki slide outline",
    slides: [
      {
        order: 1,
        title: "Wiki ders akisi",
        bullets: ["Ders page context icinde kalir", "OrkaLM kaynak state'ine otomatik yazilmaz"],
        speakerNotes: "Dersi once kavram ve plan adimi olarak anlat.",
        sourceLabel: "WikiBlock:block-1",
        checkpointQuestion: "Bu slayt OrkaLM source state'e yaziyor mu?",
        visualSuggestion: "Wiki-only lesson lane",
      },
    ],
  }),
});

const wikiUmlArtifact = baseArtifact({
  id: "artifact-wiki-uml",
  topicId,
  artifactType: "uml_diagram",
  title: "Wiki lesson UML",
  renderFormat: "mermaid",
  safeContent: "flowchart TD\n  WikiPage --> Tutor\n  WikiPage --> QuestionBank",
  sourceBasis: "wiki_grounded",
  contentJson: wikiArtifactJson({
    title: "Wiki lesson UML",
    diagramType: "flowchart",
  }),
});

function buildAudioJob(mode: SurfaceMode, body: Record<string, unknown> = {}) {
  const isOrkaLm = mode === "orkalm";
  const id = isOrkaLm ? orkalmAudioJobId : wikiAudioJobId;
  const surface = isOrkaLm ? "orkalm" : "wiki";
  const contextType = isOrkaLm ? "source_notebook" : "wiki_page";
  const script = isOrkaLm
    ? "[HOCA]: OrkaLM source notebook audio is active.\n[ASISTAN]: Caption, transcript and source-scoped study room are ready."
    : "[HOCA]: Wiki lesson audio is active.\n[ASISTAN]: Caption, transcript and wiki-scoped study room are ready.";

  return {
    id,
    status: "ready",
    script,
    speakers: ["HOCA", "ASISTAN"],
    surface,
    contextType,
    wikiPageId: isOrkaLm ? null : wikiPageId,
    sourceId: isOrkaLm ? sourceId : null,
    audioMode: typeof body.audioMode === "string" ? body.audioMode : "brief",
    dialogueFormat: "hoca_asistan_konuk",
    ttsQuality: typeof body.ttsQuality === "string" ? body.ttsQuality : "standard",
    transcript: script,
    captionTrack: "WEBVTT\n\n1\n00:00:00.000 --> 00:00:12.000\nHOCA: Contract audio is active.",
    captions: [
      {
        cueId: 1,
        speaker: "HOCA",
        text: "Contract audio is active.",
        start: "00:00:00.000",
        end: "00:00:12.000",
      },
    ],
    classroomReady: true,
    crossSurfaceSync: false,
    audioExpiresAt: "2026-06-10T09:00:00.000Z",
    audioPurgedAt: null,
    audioByteLength: 16,
    retentionNotes: [
      "audio_bytes_retention_days_7",
      isOrkaLm ? "source_evidence_limited_audio_uses_conservative_language" : "wiki_context_scoped",
    ],
    contentType: "audio/mpeg",
    fileName: `${id}.mp3`,
    downloadUrl: `/api/audio/overview/${id}/stream`,
    fallbackReason: null,
    errorMessage: null,
    createdAt: now,
    updatedAt: now,
  };
}

const notebookPack = {
  id: packId,
  topicId,
  sessionId: null,
  wikiPageId: null,
  wikiPageTitle: null,
  wikiPageKey: null,
  sourceSurface: "source",
  sourceId,
  sourceTitle: "Contract Source",
  activeLessonSnapshotId: null,
  studentContextSnapshotId: null,
  sourceEvidenceBundleId: null,
  wikiNotebookSnapshotId: null,
  planQualitySnapshotId: null,
  assessmentQualitySnapshotId: null,
  packType: "source_digest",
  packStatus: "ready",
  title: "Contract Source Notebook Pack",
  summary: "Mock-backed OrkaLM pack proving source context parity without Wiki sync.",
  sourceReadiness: "source_grounded",
  evidenceStatus: "source_grounded",
  completedConceptKeys: ["source_scope"],
  weakConceptKeys: ["no_sync"],
  misconceptionKeys: ["wiki_feed_assumption"],
  phaseScope,
  artifactIds: ["artifact-slide", "artifact-properties", "artifact-uml", "artifact-audio"],
  artifacts: [slideArtifact, propertiesArtifact, umlArtifact, activeAudioArtifact],
  nextActions: [
    { actionType: "glossary", userSafeLabel: "Glossary", priority: "high" },
    { actionType: "uml_diagram", userSafeLabel: "UML diagram", priority: "normal" },
    { actionType: "slide_deck_outline", userSafeLabel: "Slide outline", priority: "normal" },
  ],
  warnings: [],
  createdAt: now,
  updatedAt: now,
};

const wikiNotebookPack = {
  id: wikiPackId,
  topicId,
  sessionId: null,
  wikiPageId: "wiki-page-1",
  wikiPageTitle: "Wiki Contract Page",
  wikiPageKey: "wiki-contract-page",
  sourceSurface: null,
  sourceId: null,
  sourceTitle: null,
  activeLessonSnapshotId: null,
  studentContextSnapshotId: null,
  sourceEvidenceBundleId: null,
  wikiNotebookSnapshotId: null,
  planQualitySnapshotId: null,
  assessmentQualitySnapshotId: null,
  packType: "wiki_page_review",
  packStatus: "ready",
  title: "Wiki Page Review Pack",
  summary: "Mock-backed Wiki pack proving lesson context without OrkaLM source upload.",
  sourceReadiness: "wiki_grounded",
  evidenceStatus: "wiki_grounded",
  completedConceptKeys: ["wiki_scope"],
  weakConceptKeys: [],
  misconceptionKeys: [],
  phaseScope,
  artifactIds: ["artifact-wiki-study-guide", "artifact-wiki-properties", "artifact-wiki-slide", "artifact-wiki-uml"],
  artifacts: [wikiStudyGuideArtifact, wikiPropertiesArtifact, wikiSlideArtifact, wikiUmlArtifact],
  nextActions: [
    { actionType: "study_guide", userSafeLabel: "Study guide", priority: "high" },
    { actionType: "graph_view", userSafeLabel: "Graph view", priority: "normal" },
    { actionType: "slide_deck_outline", userSafeLabel: "Slide outline", priority: "normal" },
    { actionType: "uml_diagram", userSafeLabel: "UML diagram", priority: "normal" },
  ],
  warnings: [],
  createdAt: now,
  updatedAt: now,
};

const wikiPage = {
  id: "wiki-page-1",
  topicId,
  title: "Wiki Contract Page",
  pageKey: "wiki-contract-page",
  pageType: "concept",
  conceptKey: "wiki_contract",
  parentConceptKey: null,
  parentWikiPageId: null,
  status: "ready",
  sourceReadiness: "wiki_grounded",
  evidenceStatus: "wiki_grounded",
  safeSummary: "Wiki-only contract page.",
  contentReadiness: "ready",
  hasLearningContent: true,
  visibleBlockCount: 1,
  requiredBlockTypesPresent: true,
  orderIndex: 0,
  blockCount: 1,
  curation: null,
  learningSystemBinding: null,
  updatedAt: now,
  blocks: [
    {
      id: "block-1",
      wikiPageId: "wiki-page-1",
      blockType: "summary",
      type: "summary",
      title: "Wiki Contract Summary",
      content: "Wiki lesson content remains separate from OrkaLM sources.",
      source: null,
      sourceBasis: "wiki_grounded",
      conceptKey: "wiki_contract",
      misconceptionKey: null,
      quizAttemptId: null,
      sourceEvidenceBundleId: null,
      learningArtifactId: null,
      tutorTurnStateId: null,
      visibility: "student_visible",
      safetyWarnings: [],
    },
  ],
};

const wikiGraphPage = {
  id: "wiki-page-1",
  topicId,
  parentWikiPageId: null,
  planStepId: null,
  pageKey: "wiki-contract-page",
  pageType: "concept",
  conceptKey: "wiki_contract",
  parentConceptKey: null,
  title: "Wiki Contract Page",
  status: "ready",
  sourceReadiness: "wiki_grounded",
  evidenceStatus: "wiki_grounded",
  safeSummary: "Wiki-only contract page.",
  contentReadiness: "ready",
  hasLearningContent: true,
  visibleBlockCount: 1,
  requiredBlockTypesPresent: true,
  orderIndex: 0,
  blockCount: 1,
  curation: null,
  learningSystemBinding: null,
  updatedAt: now,
};

const source = {
  id: sourceId,
  topicId,
  sessionId: null,
  sourceType: "pdf",
  title: "Contract Source",
  fileName: "contract.pdf",
  pageCount: 4,
  chunkCount: 12,
  status: "ready",
  createdAt: now,
};

const sourceNotebook = {
  topicId,
  sourceId,
  surface: "source_notebook",
  title: "Contract Source Notebook",
  sourceReadiness: "source_grounded",
  evidenceStatus: "source_grounded",
  sourceCount: 1,
  readySourceCount: 1,
  chunkCount: 12,
  citationCoverage: 0.92,
  warnings: [],
  sources: [
    {
      id: sourceId,
      title: "Contract Source",
      fileName: "contract.pdf",
      sourceType: "pdf",
      sourceReadiness: "source_grounded",
      evidenceStatus: "source_grounded",
      pageCount: 4,
      chunkCount: 12,
      citationCoverage: 0.92,
      warnings: [],
    },
  ],
  linkedWikiPages: [],
  packs: [
    {
      id: packId,
      packType: "source_digest",
      packStatus: "ready",
      title: "Contract Source Notebook Pack",
      sourceId,
      wikiPageId: null,
      sourceReadiness: "source_grounded",
      evidenceStatus: "source_grounded",
      updatedAt: now,
    },
  ],
  nextActions: [
    { actionType: "glossary", userSafeLabel: "Glossary", priority: "high" },
    { actionType: "slide_deck_outline", userSafeLabel: "Slide outline", priority: "normal" },
    { actionType: "uml_diagram", userSafeLabel: "UML diagram", priority: "normal" },
  ],
  generatedAt: now,
};

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function installApiMocks(page: Page, mode: SurfaceMode) {
  const apiCalls: Array<{ method: string; path: string; search: string; postData?: string | null }> = [];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace(/^\/api/, "");
    apiCalls.push({
      method: request.method(),
      path,
      search: url.search,
      postData: request.postData(),
    });

    if (path === "/auth/refresh") {
      return fulfillJson(route, { token: "contract-token", user });
    }

    if (path === "/topics" && request.method() === "GET") {
      return fulfillJson(route, [
        {
          id: topicId,
          title: "Notebook Contract Topic",
          emoji: "N",
          category: "QA",
          parentTopicId: null,
          progressPercentage: 0,
          completedSections: 0,
          order: 0,
        },
      ]);
    }

    if (path === `/topics/${topicId}/sessions/latest`) {
      return fulfillJson(route, { message: "No session" }, 404);
    }

    if (path === "/tools/capabilities") {
      return fulfillJson(route, { tools: [], count: 0 });
    }

    if (path === "/tools/runtime/governance-summary") {
      return fulfillJson(route, { deniedCount: 0, degradedCount: 0, warnings: [] });
    }

    if (path === "/learning-runtime/health") {
      return fulfillJson(route, { status: "healthy", warnings: [] });
    }

    if (path === `/wiki/${topicId}`) {
      if (mode === "orkalm") return fulfillJson(route, []);
      return fulfillJson(route, [wikiPage]);
    }

    if (path === `/wiki/${topicId}/graph`) {
      return fulfillJson(route, {
        topicId,
        graphStatus: "ready",
        evidenceStatus: mode === "orkalm" ? "source_not_used" : "wiki_grounded",
        pages: mode === "orkalm" ? [] : [wikiGraphPage],
        links: [],
        warnings: [],
        generatedAt: now,
      });
    }

    if (path === "/wiki/page/wiki-page-1") {
      return fulfillJson(route, wikiPage);
    }

    if (path === "/wiki/page/wiki-page-1/copilot") {
      return fulfillJson(route, {
        pageId: "wiki-page-1",
        pageKey: "wiki-contract-page",
        conceptKey: "wiki_contract",
        pageTitle: "Wiki Contract Page",
        pageType: "concept",
        curationStatus: "ready",
        sourceReadiness: "wiki_grounded",
        evidenceStatus: "wiki_grounded",
        masteryStatus: "unknown",
        weakConcepts: [],
        repairState: "ready",
        artifactCount: 1,
        notebookPackStatus: "ready",
        primaryAction: null,
        suggestedActions: [],
        warnings: [],
        studentVisibleSummary: "Wiki copilot stays on lesson scope.",
        nextAction: "continue_learning",
        generatedAt: now,
      });
    }

    if (path === "/wiki/page/wiki-page-1/questions") {
      return fulfillJson(route, {
        pageId: "wiki-page-1",
        topicId,
        conceptKey: "wiki_contract",
        practiceSetId: "practice-wiki-contract",
        status: "empty",
        emptyState: "No practice questions in contract mock.",
        mode: "wiki_page_practice",
        totalQuestions: 0,
        questions: [],
      });
    }

    if (path === "/wiki/pages/wiki-page-1/source-links") {
      return fulfillJson(route, {
        topicId,
        sourceId: null,
        wikiPageId: "wiki-page-1",
        title: "Wiki page source links",
        sourceReadiness: "wiki_grounded",
        evidenceStatus: "wiki_grounded",
        confirmedLinkCount: 0,
        suggestedLinkCount: 0,
        links: [],
        warnings: [],
        generatedAt: now,
      });
    }

    if (path === `/wiki/${topicId}/knowledge-notebook`) {
      return fulfillJson(route, {
        topicId,
        title: "Wiki notebook",
        evidenceStatus: "wiki_grounded",
        sourceCoverage: mode === "orkalm" ? "source_grounded" : "wiki_only",
        conceptCoverage: "contract",
        sections: [],
        sourceWarnings: [],
        lastUpdatedAt: now,
        generatedAt: now,
      });
    }

    if (path === `/sources/topic/${topicId}`) {
      return fulfillJson(route, mode === "orkalm" ? [source] : []);
    }

    if (path === "/sources/upload" && request.method() === "POST") {
      return fulfillJson(route, source);
    }

    if (path === `/sources/topic/${topicId}/quality`) {
      return fulfillJson(route, {
        id: "source-quality-contract",
        topicId,
        sourceId: null,
        sourceCount: mode === "orkalm" ? 1 : 0,
        readySourceCount: mode === "orkalm" ? 1 : 0,
        citationCoverage: mode === "orkalm" ? 0.92 : 0,
        qualityStatus: mode === "orkalm" ? "source_grounded" : "no_sources",
        retrievalHealthStatus: mode === "orkalm" ? "healthy" : "not_applicable",
        citationCoverageStatus: mode === "orkalm" ? "healthy" : "not_applicable",
        citationSupportStatus: mode === "orkalm" ? "supported" : "not_applicable",
        retrievalRunCount: mode === "orkalm" ? 4 : 0,
        emptyRunCount: 0,
        citationCheckCount: mode === "orkalm" ? 4 : 0,
        unsupportedCitationCount: 0,
        citationMissingCount: 0,
        averageContextRelevance: mode === "orkalm" ? 0.88 : 0,
        evidenceQuality: null,
        recentRetrievalRuns: [],
        recentCitationChecks: [],
        warnings: [],
        generatedAt: now,
      });
    }

    if (path === `/sources/topic/${topicId}/notebook`) {
      return fulfillJson(route, mode === "orkalm" ? sourceNotebook : { ...sourceNotebook, sourceId: null, sourceCount: 0, readySourceCount: 0, chunkCount: 0, sources: [] });
    }

    if (path === `/sources/${sourceId}/notebook`) {
      return fulfillJson(route, sourceNotebook);
    }

    if (path === `/sources/${sourceId}/concept-links`) {
      return fulfillJson(route, {
        topicId,
        sourceId,
        wikiPageId: null,
        title: "Source concept suggestions",
        sourceReadiness: "source_grounded",
        evidenceStatus: "source_grounded",
        confirmedLinkCount: 0,
        suggestedLinkCount: 1,
        warnings: [],
        generatedAt: now,
        links: [
          {
            sourceId,
            sourceTitle: "Contract Source",
            sourcePageId: null,
            conceptKey: "source_scope",
            conceptTitle: "Source Scope",
            wikiPageId: null,
            linkType: "source_mentions",
            confidence: "medium",
            confidenceScore: 0.7,
            basis: "suggestion_only",
            evidenceStatus: "source_grounded",
            sourceReadiness: "source_grounded",
            isSuggestion: true,
            warnings: [],
            createdAt: now,
            updatedAt: now,
          },
        ],
      });
    }

    if (path === `/sources/topic/${topicId}/concept-graph`) {
      return fulfillJson(route, {
        topicId,
        graphStatus: "ready",
        warnings: [],
        generatedAt: now,
        nodes: [
          {
            id: sourceId,
            nodeType: "source",
            label: "Contract Source",
            sourceId,
            wikiPageId: null,
            conceptKey: null,
            status: "ready",
            sourceReadiness: "source_grounded",
            evidenceStatus: "source_grounded",
          },
          {
            id: "concept-source-scope",
            nodeType: "concept",
            label: "Source Scope",
            sourceId: null,
            wikiPageId: null,
            conceptKey: "source_scope",
            status: "suggested",
            sourceReadiness: "source_grounded",
            evidenceStatus: "source_grounded",
          },
        ],
        edges: [
          {
            sourceNodeId: sourceId,
            targetNodeId: "concept-source-scope",
            linkType: "source_mentions",
            confidence: "medium",
            confidenceScore: 0.7,
            basis: "suggestion_only",
            isSuggestion: true,
            warnings: [],
          },
        ],
      });
    }

    if (path === `/sources/topic/${topicId}/citation-review`) {
      return fulfillJson(route, {
        topicId,
        sourceId: null,
        reviewStatus: "source_grounded",
        coverage: {
          totalCitationChecks: 4,
          supportedCount: 4,
          unsupportedCount: 0,
          missingCount: 0,
          staleCount: 0,
          needsReviewCount: 0,
          coverageRatio: 1,
          coverageStatus: "source_grounded",
        },
        items: [],
        warnings: [],
        generatedAt: now,
      });
    }

    if (path === "/sources/question-threads") {
      return fulfillJson(route, { count: 0, items: [] });
    }

    if (path === "/sources/study-summary") {
      return fulfillJson(route, {
        topicId,
        sourceId: mode === "orkalm" ? sourceId : null,
        wikiPageId: null,
        sourceCount: mode === "orkalm" ? 1 : 0,
        threadCount: 0,
        turnCount: 0,
        reviewedCount: 0,
        needsReviewCount: 0,
        degradedCount: 0,
        citationWarningCount: 0,
        relatedConceptCount: 1,
        comparedSourceCount: 0,
        sourceReadiness: mode === "orkalm" ? "source_grounded" : "no_sources",
        evidenceStatus: mode === "orkalm" ? "source_grounded" : "no_sources",
        studyStatus: "ready",
        recommendedNextAction: "review_source_pack",
        nextActions: ["review_source_pack"],
        recentQuestions: [],
        warnings: [],
        generatedAt: now,
      });
    }

    if (path === `/notebook-studio/topic/${topicId}/packs`) {
      const surface = url.searchParams.get("surface");
      const requestedSourceId = url.searchParams.get("sourceId");
      if (mode === "orkalm") {
        if (surface === "source") {
          expect(requestedSourceId).toBe(sourceId);
          return fulfillJson(route, { count: 1, items: [notebookPack] });
        }
        return fulfillJson(route, { count: 0, items: [] });
      }
      if (url.searchParams.get("wikiPageId") === "wiki-page-1") {
        return fulfillJson(route, { count: 1, items: [wikiNotebookPack] });
      }
      expect(surface).not.toBe("source");
      return fulfillJson(route, { count: 0, items: [] });
    }

    if (path === `/notebook-studio/packs/${packId}/export/preview`) {
      return fulfillJson(route, {
        packId,
        slideDeckArtifactId: "artifact-slide",
        surface: "orkalm",
        contextType: "source_notebook",
        wikiPageId: null,
        sourceId,
        exportScope: "orkalm_source_export_scope",
        sourceUploadAllowed: true,
        crossSurfaceSync: false,
        deckTitle: "Contract Source Notebook Pack",
        slideCount: 1,
        exportReadiness: "ready",
        sourceBasis: "source_grounded",
        sourceReadiness: "source_grounded",
        accessibilitySummary: "Speaker notes and checkpoint questions are present.",
        warnings: [],
        slides: [
          {
            slideId: "slide-1",
            order: 1,
            title: "Kaynak defteri akisi",
            bullets: ["Upload OrkaLM icinde kalir"],
            sourceLabel: "contract.pdf p.2",
            checkpointQuestion: "Bu slayt Wiki state'e yaziyor mu?",
            hasSpeakerNotes: true,
          },
        ],
        templateKeys: ["source_export", "source_slide_outline"],
        searchFilterKeys: ["surface:orkalm", "context_type:source_notebook", "cross_surface_sync:false"],
        internalConnectionKeys: ["source_notebook", "citation", "source_qa", "source_practice", "cross_surface_sync:disabled"],
        phaseScope,
        generatedAt: now,
      });
    }

    if (path === `/notebook-studio/packs/${wikiPackId}/export/preview`) {
      return fulfillJson(route, {
        packId: wikiPackId,
        slideDeckArtifactId: null,
        surface: "wiki",
        contextType: "wiki_page",
        wikiPageId: "wiki-page-1",
        sourceId: null,
        exportScope: "wiki_lesson_export_scope",
        sourceUploadAllowed: false,
        crossSurfaceSync: false,
        deckTitle: "Wiki Page Review Pack",
        slideCount: 0,
        exportReadiness: "wiki_preview_ready",
        sourceBasis: "wiki_grounded",
        sourceReadiness: "wiki_grounded",
        accessibilitySummary: "Wiki preview is scoped to the active lesson page.",
        warnings: [],
        slides: [],
        templateKeys: ["wiki_export", "wiki_page_template"],
        searchFilterKeys: ["surface:wiki", "context_type:wiki_page", "cross_surface_sync:false"],
        internalConnectionKeys: ["wiki_page", "plan_step", "tutor_trace", "question_bank_trace", "wiki_learning_trace", "cross_surface_sync:disabled"],
        phaseScope,
        generatedAt: now,
      });
    }

    if (path === "/audio/overview" && request.method() === "POST") {
      const body = request.postDataJSON() as Record<string, unknown>;
      if (mode === "orkalm") {
        expect(body.surface).toBe("orkalm");
        expect(body.sourceId).toBe(sourceId);
        expect(body.wikiPageId ?? null).toBeNull();
      } else {
        expect(body.surface).toBe("wiki");
        expect(body.wikiPageId).toBe(wikiPageId);
        expect(body.sourceId ?? null).toBeNull();
      }

      return fulfillJson(route, buildAudioJob(mode, body));
    }

    if (path === `/audio/overview/${orkalmAudioJobId}` || path === `/audio/overview/${wikiAudioJobId}`) {
      return fulfillJson(route, buildAudioJob(mode));
    }

    if (path === `/audio/overview/${orkalmAudioJobId}/stream` || path === `/audio/overview/${wikiAudioJobId}/stream`) {
      return route.fulfill({
        status: 200,
        contentType: "audio/mpeg",
        body: Buffer.from("ID3contract-audio"),
      });
    }

    if (path === "/classroom/session" && request.method() === "POST") {
      const body = request.postDataJSON() as Record<string, unknown>;
      if (mode === "orkalm") {
        expect(body.surface).toBe("orkalm");
        expect(body.audioOverviewJobId).toBe(orkalmAudioJobId);
        expect(body.sourceId).toBe(sourceId);
        expect(body.wikiPageId ?? null).toBeNull();
      } else {
        expect(body.surface).toBe("wiki");
        expect(body.audioOverviewJobId).toBe(wikiAudioJobId);
        expect(body.wikiPageId).toBe(wikiPageId);
        expect(body.sourceId ?? null).toBeNull();
      }
      expect(body.transcript).toContain("[HOCA]");

      return fulfillJson(route, {
        id: classroomSessionId,
        topicId,
        sessionId: null,
        audioOverviewJobId: body.audioOverviewJobId,
        surface: body.surface,
        contextType: mode === "orkalm" ? "source_notebook" : "wiki_page",
        wikiPageId: mode === "orkalm" ? null : wikiPageId,
        sourceId: mode === "orkalm" ? sourceId : null,
        audioMode: body.audioMode ?? "brief",
        crossSurfaceSync: false,
        internalConnections: mode === "orkalm"
          ? ["source_notebook", "citation", "source_qa"]
          : ["wiki_page", "tutor_trace", "question_bank_trace"],
        status: "active",
        createdAt: now,
        updatedAt: now,
      });
    }

    if (path === `/classroom/${classroomSessionId}/ask` && request.method() === "POST") {
      const body = request.postDataJSON() as Record<string, unknown>;
      expect(String(body.question ?? "")).toContain("anlamadim");
      expect(String(body.activeSegment ?? "")).toContain("[HOCA]");

      return fulfillJson(route, {
        classroomSessionId,
        interactionId: "interaction-contract",
        answer: "[HOCA]: Takildigin bolumu daha sade bir ornekle aciyorum.\n[ASISTAN]: Once tek kavrami tutup sonra mini soruya gecelim.",
        speakers: ["HOCA", "ASISTAN"],
        surface: mode,
        contextType: mode === "orkalm" ? "source_notebook" : "wiki_page",
        wikiPageId: mode === "orkalm" ? null : wikiPageId,
        sourceId: mode === "orkalm" ? sourceId : null,
        audioMode: "brief",
        audioQueued: false,
        browserTtsFallback: true,
      });
    }

    if (path === "/classroom/interaction/interaction-contract/audio") {
      return route.fulfill({
        status: 404,
        contentType: "application/json",
        body: JSON.stringify({ message: "No generated interaction audio in browser contract." }),
      });
    }

    if (path === "/learning-artifacts") {
      return fulfillJson(route, { count: 0, items: [] });
    }

    if (path === `/sources/topic/${topicId}/evidence-bundle`) {
      return fulfillJson(route, {
        topicId,
        evidenceStatus: mode === "orkalm" ? "source_grounded" : "no_sources",
        sourceReadiness: mode === "orkalm" ? "source_grounded" : "no_sources",
        warnings: [],
        generatedAt: now,
      });
    }

    if (path === `/learning/topic/${topicId}/summary`) {
      return fulfillJson(route, {
        topicId,
        totalAttempts: 0,
        correctAttempts: 0,
        accuracy: 0,
        weakSkills: [],
        recentSignals: [],
        cache: null,
      });
    }

    if (path === `/quiz/history/${topicId}`) {
      return fulfillJson(route, []);
    }

    if (path === `/wiki/${topicId}/briefing`) {
      return fulfillJson(route, {
        topicId,
        topicTitle: "Notebook Contract Topic",
        tldr: "Contract briefing",
        keyTakeaways: [],
        suggestedQuestions: [],
        generatedAt: now,
      });
    }

    if (path === `/wiki/${topicId}/glossary`) return fulfillJson(route, { topicId, items: [], generatedAt: now });
    if (path === `/wiki/${topicId}/timeline`) return fulfillJson(route, { topicId, items: [], generatedAt: now });
    if (path === `/wiki/${topicId}/mindmap`) return fulfillJson(route, { topicId, mermaid: "", nodes: [], generatedAt: now });
    if (path === `/wiki/${topicId}/study-cards`) return fulfillJson(route, { topicId, cards: [], generatedAt: now });
    if (path === `/wiki/${topicId}/recommendations`) return fulfillJson(route, { topicId, items: [], generatedAt: now });

    if (path.startsWith("/learning-snapshots/")) return fulfillJson(route, null);
    if (path.startsWith("/plan-quality/")) return fulfillJson(route, null);
    if (path === "/tutor/next-actions") return fulfillJson(route, []);
    if (path.startsWith("/tutor/policy/")) {
      return fulfillJson(route, {
        sourceReadiness: mode === "orkalm" ? "source_grounded" : "wiki_grounded",
        nextActions: [],
      });
    }
    if (path.startsWith("/tutor/")) return fulfillJson(route, []);
    if (path === "/learning/signal") return fulfillJson(route, { ok: true });

    return fulfillJson(route, request.method() === "GET" ? {} : { ok: true });
  });

  return apiCalls;
}

async function bootAuthenticatedApp(page: Page, mode: SurfaceMode) {
  await page.addInitScript(
    ({ activeMode, activeTopicId, activeUser }) => {
      localStorage.setItem("orka_token", "contract-token");
      localStorage.setItem("orka_user", JSON.stringify(activeUser));
      localStorage.setItem("orka_active_topic_id", activeTopicId);
      localStorage.setItem("orka_wiki_topic_id", activeTopicId);
      localStorage.setItem("orka_active_view", activeMode === "orkalm" ? "sources" : "wiki");
      localStorage.setItem(`orka_premium_tour_seen_v3_${activeUser.id}`, "true");
      const voice = {
        default: true,
        lang: "tr-TR",
        localService: true,
        name: "Contract Turkish Voice",
        voiceURI: "contract-tr",
      };
      Object.defineProperty(window, "speechSynthesis", {
        configurable: true,
        value: {
          onvoiceschanged: null,
          getVoices: () => [voice],
          speak: (utterance: SpeechSynthesisUtterance) => {
            window.setTimeout(() => utterance.onend?.(new Event("end") as SpeechSynthesisEvent), 5);
          },
          cancel: () => undefined,
          pause: () => undefined,
          resume: () => undefined,
        },
      });
    },
    { activeMode: mode, activeTopicId: topicId, activeUser: user },
  );
}

async function exerciseAudioStudyRoom(
  page: Page,
  apiCalls: Array<{ method: string; path: string; search: string; postData?: string | null }>,
  mode: SurfaceMode,
) {
  const expectedAudioJobId = mode === "orkalm" ? orkalmAudioJobId : wikiAudioJobId;
  const expectedContextType = mode === "orkalm" ? "source_notebook" : "wiki_page";

  await page.getByTestId("audio-overview-create").click();
  await expect.poll(() => apiCalls.some((call) => call.method === "POST" && call.path === "/audio/overview")).toBe(true);
  await expect(page.locator('audio track[kind="captions"]')).toHaveCount(1);
  await expect(page.getByText(expectedContextType).last()).toBeVisible();
  await expect(page.getByText("caption ready").last()).toBeVisible();

  await page.getByTestId("audio-study-room-open").click();
  await expect(page.getByTestId("audio-study-room-question")).toBeVisible();
  await page.getByTestId("audio-study-room-question").fill("Burayi anlamadim");
  await page.getByTestId("audio-study-room-ask").click();

  await expect.poll(() => apiCalls.some((call) => call.method === "POST" && call.path === "/classroom/session")).toBe(true);
  await expect.poll(() => apiCalls.some((call) => call.method === "POST" && call.path === `/classroom/${classroomSessionId}/ask`)).toBe(true);

  const sessionCalls = apiCalls.filter((call) => call.method === "POST" && call.path === "/classroom/session");
  const sessionPayload = JSON.parse(sessionCalls[sessionCalls.length - 1]?.postData ?? "{}") as Record<string, unknown>;
  expect(sessionPayload.surface).toBe(mode);
  expect(sessionPayload.audioOverviewJobId).toBe(expectedAudioJobId);
  expect(sessionPayload.audioMode).toBe("brief");
  if (mode === "orkalm") {
    expect(sessionPayload.sourceId).toBe(sourceId);
    expect(sessionPayload.wikiPageId ?? null).toBeNull();
  } else {
    expect(sessionPayload.wikiPageId).toBe(wikiPageId);
    expect(sessionPayload.sourceId ?? null).toBeNull();
  }

  const askCalls = apiCalls.filter((call) => call.method === "POST" && call.path === `/classroom/${classroomSessionId}/ask`);
  const askPayload = JSON.parse(askCalls[askCalls.length - 1]?.postData ?? "{}") as Record<string, unknown>;
  expect(String(askPayload.question ?? "")).toContain("anlamadim");
  expect(String(askPayload.activeSegment ?? "")).toContain("[HOCA]");
}

test.describe("Notebook Studio Wiki/OrkaLM contract", () => {
  test("renders OrkaLM source notebook features, upload, and active audio without Wiki sync", async ({ page }) => {
    const apiCalls = await installApiMocks(page, "orkalm");
    const consoleErrors: string[] = [];
    page.on("console", (message) => {
      if (message.type() === "error") consoleErrors.push(message.text());
    });

    await bootAuthenticatedApp(page, "orkalm");
    await page.goto("/app");

    await expect(page.getByText("OrkaLM kaynak defteri")).toBeVisible({ timeout: 20000 });
    await expect(page.getByRole("button", { name: /PDF \/ Kaynak/i })).toBeVisible();
    await expect(page.getByRole("button", { name: "Source Pack" })).toBeVisible();

    const artifactPreview = page.locator("article").filter({ hasText: "OrkaLM slide outline" });
    await expect(artifactPreview.getByText("Surface", { exact: true })).toBeVisible();
    await expect(artifactPreview).toContainText("orkalm");
    await expect(artifactPreview.getByText("Context", { exact: true })).toBeVisible();
    await expect(artifactPreview).toContainText("source_notebook");
    await expect(artifactPreview).toContainText("cross-surface sync kapali");
    await expect(artifactPreview).toContainText("phase 7 audio active");
    await expect(artifactPreview.getByText("Slide preview", { exact: true })).toBeVisible();
    await expect(artifactPreview.getByText("speaker notes", { exact: true })).toBeVisible();
    await expect(artifactPreview.getByText("checkpoint", { exact: true })).toBeVisible();

    await page.getByRole("button", { name: /OrkaLM properties and graph contract/i }).click();
    await expect(page.getByText("Properties contract", { exact: true })).toBeVisible();
    await expect(page.getByText("Scoped graph contract", { exact: true })).toBeVisible();
    await expect(page.getByText(/cross-surface 0/).first()).toBeVisible();
    await expect(page.getByText("Export readiness", { exact: true }).first()).toBeVisible();

    await page.getByRole("button", { name: /Active audio overview/i }).click();
    await expect(page.getByText(/Sesli ders paketi aktif/i).first()).toBeVisible();
    await expect(page.getByText(/caption ready/i).first()).toBeVisible();

    await exerciseAudioStudyRoom(page, apiCalls, "orkalm");

    await expect(page.getByText("Write to Wiki")).toHaveCount(0);

    const fileChooserPromise = page.waitForEvent("filechooser");
    await page.getByRole("button", { name: /PDF \/ Kaynak/i }).click();
    const fileChooser = await fileChooserPromise;
    await fileChooser.setFiles({
      name: "contract-upload.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from("contract upload smoke"),
    });
    await expect.poll(() => apiCalls.some((call) => call.method === "POST" && call.path === "/sources/upload")).toBe(true);

    const packRequest = apiCalls.find((call) => {
      if (call.path !== `/notebook-studio/topic/${topicId}/packs`) return false;
      const params = new URLSearchParams(call.search);
      return params.get("surface") === "source";
    });
    expect(packRequest?.search).toContain("surface=source");
    expect(packRequest?.search).toContain(`sourceId=${sourceId}`);
    expect(apiCalls.some((call) => call.path.includes("/wiki-trace"))).toBe(false);
    expect(apiCalls.some((call) => call.method !== "GET" && call.path.startsWith("/wiki/"))).toBe(false);
  });

  test("keeps source upload hidden when the same panel runs in Wiki mode", async ({ page }) => {
    const apiCalls = await installApiMocks(page, "wiki");
    const pageErrors: string[] = [];
    const consoleErrors: string[] = [];
    page.on("console", (message) => {
      if (message.type() === "error") consoleErrors.push(message.text());
    });
    page.on("pageerror", (error) => pageErrors.push(error.stack ?? error.message));
    await bootAuthenticatedApp(page, "wiki");

    await page.goto("/app");

    await expect(page.locator("body")).toContainText(/Aktif Wiki sayfasi|Beklenmeyen bir hata/, { timeout: 20000 });
    if (await page.getByText("Beklenmeyen bir hata").isVisible().catch(() => false)) {
      throw new Error(
        [
          ...pageErrors,
          ...consoleErrors,
          `apiCalls=${JSON.stringify(apiCalls, null, 2)}`,
        ].filter(Boolean).join("\n") || "Wiki mode rendered the error boundary without diagnostics.",
      );
    }

    try {
      await expect(page.getByText("Aktif Wiki sayfasi").first()).toBeVisible({ timeout: 20000 });
    } catch (error) {
      throw new Error(
        [
          error instanceof Error ? error.message : String(error),
          ...pageErrors,
          ...consoleErrors,
          `body=${(await page.locator("body").innerText().catch(() => "")).slice(0, 2000)}`,
          `apiCalls=${JSON.stringify(apiCalls, null, 2)}`,
        ].filter(Boolean).join("\n"),
      );
    }
    await expect(page.getByRole("button", { name: /PDF \/ Kaynak/i })).toHaveCount(0);
    await expect(page.getByText("OrkaLM kaynak defteri")).toHaveCount(0);
    await expect(page.getByText("Wiki burada kaynak yukletmez")).toBeVisible();
    await expect(page.getByRole("button", { name: "Calisma rehberi", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Glossary" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Timeline" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Slayt taslagi", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "UML / Mermaid", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sesli anlatim", exact: true })).toBeVisible();

    const wikiArtifactPreview = page.locator("article").filter({ hasText: "Wiki Study Guide" });
    await expect(wikiArtifactPreview.getByText("Surface", { exact: true })).toBeVisible();
    await expect(wikiArtifactPreview).toContainText("wiki");
    await expect(wikiArtifactPreview.getByText("Context", { exact: true })).toBeVisible();
    await expect(wikiArtifactPreview).toContainText("wiki_page");
    await expect(wikiArtifactPreview).toContainText("cross-surface sync kapali");
    await expect(wikiArtifactPreview).toContainText("phase 7 audio active");

    await page.getByRole("button", { name: "Preview" }).click();
    await expect(page.getByText("wiki_lesson_export_scope").first()).toBeVisible();
    await expect(page.getByText("wiki_preview_ready").first()).toBeVisible();

    await page.getByRole("button", { name: /Wiki properties and graph contract/i }).click();
    await expect(page.getByText("Properties contract", { exact: true })).toBeVisible();
    await expect(page.getByText("Scoped graph contract", { exact: true })).toBeVisible();
    await expect(page.getByText(/cross-surface 0/).first()).toBeVisible();

    await page.getByRole("button", { name: /Wiki slide outline/i }).click();
    await expect(page.getByText("Slide preview", { exact: true })).toBeVisible();
    await expect(page.getByText("speaker notes", { exact: true })).toBeVisible();
    await expect(page.getByText("checkpoint", { exact: true })).toBeVisible();

    await page.getByRole("button", { name: /Wiki lesson UML/i }).click();
    await expect(page.locator("article").filter({ hasText: "Wiki lesson UML" })).toContainText("UML / Mermaid");

    await exerciseAudioStudyRoom(page, apiCalls, "wiki");

    const packRequest = apiCalls.find((call) => call.path === `/notebook-studio/topic/${topicId}/packs`);
    expect(packRequest?.search ?? "").not.toContain("surface=source");
    expect(apiCalls.some((call) => call.path === "/sources/upload")).toBe(false);
  });
});
