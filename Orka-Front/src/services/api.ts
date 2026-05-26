import axios, {
  type AxiosInstance,
  type InternalAxiosRequestConfig,
  type AxiosResponse,
} from "axios";
import toast from "react-hot-toast";
import type { ActiveLessonSnapshotDto, ActiveLessonSnapshotRequestDto, AdaptiveAssessmentNextItem, AdaptiveAssessmentSession, AdaptiveStudyPlanDto, AdaptiveStudyPlanRequestDto, AdvanceQuestionReviewStageDto, AgenticTrustCheckRequestDto, AgenticTrustCheckResultDto, AgenticTrustRuntimeSummaryDto, AssessmentBlueprintDto, AssessmentBlueprintRequestDto, AssessmentCalibrationRun, AssessmentQualityEvaluationDto, AssessmentQualityEvaluationRequestDto, AssignQuestionReviewerDto, CentralExamBlueprintCoverageDto, CentralExamCountdownDto, CentralExamDenemeBlueprintDto, CentralExamDenemeResultDto, CentralExamDenemeSessionDto, CentralExamDenemeStartRequestDto, CentralExamDenemeSubmitRequestDto, CentralExamDto, CentralExamPracticeEntryDto, CentralExamQualityOverviewDto, CentralExamStudyHomeDto, CitationReviewResultDto, ContentLicenseReviewDto, CreateCurriculumNodeDto, CreateCurriculumOutcomeMappingDto, CreateCurriculumVersionDto, CreateQuestionAssetDto, CreateQuestionContentBlockDto, CreateQuestionDto, CreateQuestionOptionContentBlockDto, CreateQuestionStimulusDto, CreateWikiBlockRequestDto, CurriculumNodeDto, CurriculumOutcomeMappingDto, CurriculumOutcomeSourceDto, CurriculumVersionDto, DeprecateCurriculumVersionDto, EvidenceQualityDto, ExamDefinitionDto, ExamTreeImportDto, KorteksConsumerContexts, KorteksEvidenceSummary, KorteksResearchSynthesis, KorteksResearchWorkflow, KorteksSynthesisIssue, LearningArtifactDto, LearningArtifactListDto, LearningArtifactRefreshRequestDto, LearningArtifactRequestDto, LearningArtifactSafetyDto, LearningMemoryLiteDto, LearningNotebookArtifactRequestDto, LearningNotebookPackDto, LearningNotebookPackListDto, LearningNotebookPackRequestDto, LearningQualityReport, LearningRuntimeCorrelationDto, LearningRuntimeFlowSummaryDto, LearningRuntimeHealthDto, LearningRuntimePrivacyCheckDto, LearningRuntimePrivacyCheckRequestDto, LearningRuntimeTraceDto, LearningRuntimeTracesResponseDto, LearningSignalConfidenceDto, MarkSourceStaleRequestDto, MisconceptionSignalDto, MultiSourceCompareRequestDto, MultiSourceCompareResultDto, NotebookExportRequestDto, NotebookExportResultDto, NotebookSlideExportPreviewDto, PlanQualityEvaluationDto, PlanQualityEvaluationRequestDto, PlanReadinessDto, PracticeResultDto, PracticeSessionDto, PracticeStartRequestDto, PracticeSubmitRequestDto, ProductionReadiness, PublishQuestionContentDto, QuestionAssetDto, QuestionBankFilterDto, QuestionContentVersionDto, QuestionDraftApprovalDto, QuestionDraftApprovalResultDto, QuestionDraftGenerationRequestDto, QuestionDraftPreviewDto, QuestionImportApprovalDto, QuestionImportPackageDto, QuestionImportPreviewDto, QuestionImportRequestDto, QuestionImportResultDto, QuestionImportTextAdapterRequestDto, QuestionItemAnalyticsDto, QuestionItemDto, QuestionPublishReadinessDto, QuestionQualityReviewSignalDto, QuestionReviewWorkflowDto, QuestionStimulusDto, QuestionStimulusLinkDto, RecalculateExamAnalyticsResultDto, RecalculateQuestionAnalyticsResultDto, RegisterSourceRegistryItemDto, RejectQuestionReviewDto, RemediationSeedDto, RetireQuestionDto, ReviewSourceLicenseDto, SourceCitationSetValidationDto, SourceConceptGraphDto, SourceConceptLinkSummaryDto, SourceEvidenceBundleDto, SourceEvidenceBundleRequestDto, SourceLifecycleSummaryDto, SourceNotebookDto, SourceQuestionFollowUpRequestDto, SourceQuestionRequestDto, SourceQuestionResponseDto, SourceQuestionReviewStateDto, SourceQuestionThreadDto, SourceQuestionThreadListDto, SourceQuestionThreadRequestDto, SourceQualityReportDto, SourceStudySummaryDto, SourceRegistryItemDto, StandardsExportRun, StandardsSummary, StandardsValidationRun, StudentContextSnapshotDto, StudentContextSnapshotRequestDto, StudyIntentPreview, SubmitQuestionReviewDto, SupersedeCurriculumVersionDto, TeachingArtifact, ToolCapabilitiesResponse, ToolCapability, ToolGovernanceSummary, ToolRuntimeDecision, ToolRuntimeRequest, ToolRuntimeTrace, ToolRuntimeTracesResponse, TutorNextLearningActionDto, TutorResponsePolicyDto, TutorResponsePolicyRequestDto, TutorResponseQualityEvaluationDto, TutorResponseQualityEvaluationRequestDto, TutorTraceTimeline, UpdateQuestionDto, ValidateSourceCitationSetRequestDto, VerifySourceRegistryItemDto, WikiBlockDto, WikiCopilotContextDto, WikiGraphDto, WikiGraphSyncRequestDto, WikiGraphSyncResultDto, WikiKnowledgeNotebookDto, OrkaLearningContractQuery, OrkaMissionControlDto, OrkaStudyCoachDto, OrkaLearningStateDto, OrkaNotebookStudioProDto, OrkaStudyRoomDto, OrkaStudyRoomStartRequestDto, OrkaStudyRoomCheckpointRequestDto, OrkaExamWarRoomDto, OrkaCodeLearningIdeDto, OrkaSourceWikiProDto, LongTermLearningProfileDto, ExamLearningProfileDto, SourceWikiIntelligenceProfileDto } from "@/lib/types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface AuthTokens {
  token: string;
  refreshToken?: string;
}

export interface AuthUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  plan?: string;
  isAdmin?: boolean;
  dailyMessageCount?: number;
  dailyLimit?: number;
  settings?: {
    theme: string;
    language: string;
    fontSize: string;
    quizReminders: boolean;
    weeklyReport: boolean;
    newContentAlerts: boolean;
    soundsEnabled: boolean;
  };
}

export interface AuthResponse extends AuthTokens {
  user: AuthUser;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}

// ---------------------------------------------------------------------------
// Token helpers  (localStorage)
// ---------------------------------------------------------------------------

const TOKEN_KEY = "orka_token";
const USER_KEY = "orka_user";
export const API_ORIGIN =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, "") ?? "";

export const buildApiUrl = (path: string) => {
  const normalized = path.startsWith("/") ? path : `/${path}`;
  return `${API_ORIGIN}${normalized}`;
};

export const storage = {
  getToken: () => localStorage.getItem(TOKEN_KEY),
  getUser: (): AuthUser | null => {
    const raw = localStorage.getItem(USER_KEY);
    return raw ? (JSON.parse(raw) as AuthUser) : null;
  },
  save: (data: AuthResponse) => {
    localStorage.setItem(TOKEN_KEY, data.token);
    localStorage.setItem(USER_KEY, JSON.stringify(data.user));
  },
  clear: () => {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    // Clear Orka session view state so another user never sees the previous user's context.
    localStorage.removeItem("orka_active_topic_id");
    localStorage.removeItem("orka_active_view");
    localStorage.removeItem("orka_wiki_topic_id");
  },
};

// ---------------------------------------------------------------------------
// Axios instance
// Requests go to /api/... — Vite proxy forwards them to localhost:5065/api/... by default.
// ---------------------------------------------------------------------------

const api: AxiosInstance = axios.create({
  baseURL: buildApiUrl("/api"),
  headers: { "Content-Type": "application/json" },
  withCredentials: true,
});

type OrkaAxiosConfig = InternalAxiosRequestConfig & {
  suppressErrorToast?: boolean;
};

const isAuthUrl = (url?: string) => {
  const normalized = (url ?? "").toLowerCase();
  return normalized.startsWith("/auth/") || normalized.startsWith("/api/auth/");
};

const redirectToLogin = () => {
  if (typeof window !== "undefined" && !window.location.pathname.startsWith("/login")) {
    window.location.href = "/login";
  }
};

const handleAuthFailure = () => {
  storage.clear();
  delete api.defaults.headers.common.Authorization;
  redirectToLogin();
};

// Request interceptor — attach Bearer token
api.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = storage.getToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// Response interceptor — transparent token refresh on 401
let isRefreshing = false;
let pendingQueue: Array<{
  resolve: (token: string) => void;
  reject: (err: unknown) => void;
}> = [];

const flushQueue = (err: unknown, token: string | null = null) => {
  pendingQueue.forEach((p) => (err ? p.reject(err) : p.resolve(token!)));
  pendingQueue = [];
};

const refreshAccessToken = async () => {
  if (isRefreshing) {
    return new Promise<string>((resolve, reject) => {
      pendingQueue.push({ resolve, reject });
    });
  }

  isRefreshing = true;
  try {
    const { data } = await axios.post<AuthTokens>(
      buildApiUrl("/api/auth/refresh"),
      {},
      { withCredentials: true }
    );
    localStorage.setItem(TOKEN_KEY, data.token);
    api.defaults.headers.common.Authorization = `Bearer ${data.token}`;
    flushQueue(null, data.token);
    return data.token;
  } catch (refreshError) {
    flushQueue(refreshError);
    handleAuthFailure();
    throw refreshError;
  } finally {
    isRefreshing = false;
  }
};

export const authenticatedFetch = async (path: string, init: RequestInit = {}) => {
  const run = (token: string) => {
    const headers = new Headers(init.headers);
    headers.set("Authorization", `Bearer ${token}`);
    return fetch(buildApiUrl(path), { ...init, headers, credentials: init.credentials ?? "include" });
  };

  let token = storage.getToken();
  if (!token) {
    token = await refreshAccessToken();
  }

  let response = await run(token);
  if (response.status !== 401 || isAuthUrl(path)) {
    return response;
  }

  token = await refreshAccessToken();
  response = await run(token);
  return response;
};

api.interceptors.response.use(
  (res: AxiosResponse) => res,
  async (error) => {
    const original = error.config as OrkaAxiosConfig & {
      _retry?: boolean;
    };
    const isAuthRoute = isAuthUrl(original?.url);

    if (original && error.response?.status === 401 && !isAuthRoute && !original._retry) {
      original._retry = true;
      try {
        const token = await refreshAccessToken();
        original.headers.Authorization = `Bearer ${token}`;
        return api(original);
      } catch (refreshError) {
        return Promise.reject(refreshError);
      }
    }

    // Global error notification, excluding auth routes.
    if (!isAuthRoute && !original?.suppressErrorToast) {
      const status = error.response?.status;
      const url = original?.url ?? "bilinmeyen endpoint";
      const endpointLabel = url.split("/").slice(-2).join("/");
      const correlationId = error.response?.headers?.["x-correlation-id"];
      const suffix = correlationId ? ` · id: ${correlationId}` : "";

      if (!error.response) {
        toast.error(`Sunucuya bağlanılamıyor (${endpointLabel})`, { id: `net-${endpointLabel}` });
      } else if (status === 404) {
        toast.error(`Hata: ${endpointLabel} bulunamadı (404)${suffix}`, { id: `404-${endpointLabel}` });
      } else if (status && status >= 500) {
        const message = (error.response?.data as { message?: string } | undefined)?.message ?? "Sunucu hatası";
        toast.error(`${endpointLabel}: ${message} (${status})${suffix}`, { id: `5xx-${endpointLabel}` });
      }
    }

    return Promise.reject(error);
  }
);

// ---------------------------------------------------------------------------
// API namespaces
// ---------------------------------------------------------------------------

export const AuthAPI = {
  login: (data: LoginRequest) =>
    api.post<AuthResponse>("/auth/login", data),
  register: (data: RegisterRequest) =>
    api.post<AuthResponse>("/auth/register", data),
  logout: () =>
    api.post("/auth/logout", {}),
  refresh: () =>
    api.post<AuthTokens>("/auth/refresh", {}),
};

export const UserAPI = {
  getMe: () => api.get<AuthUser>("/user/me"),
  getGamification: () => api.get("/user/gamification"),
  updateProfile: (data: { firstName?: string; lastName?: string; email?: string }) =>
    api.patch("/user/profile", data),
  updateSettings: (data: {
    theme?: string;
    language?: string;
    fontSize?: string;
    quizReminders?: boolean;
    weeklyReport?: boolean;
    newContentAlerts?: boolean;
    soundsEnabled?: boolean;
  }) => api.patch("/user/settings", data),
  deleteAccount: () => api.delete("/user/account"),
};

export const TopicsAPI = {
  getAll: () => api.get("/topics"),
  create: (data: { title: string; emoji: string; category: string }) =>
    api.post("/topics", data),
  getOne: (id: string) => api.get(`/topics/${id}`),
  update: (id: string, data: Partial<{ title: string; emoji: string }>) =>
    api.patch(`/topics/${id}`, data),
  delete: (id: string) => api.delete(`/topics/${id}`),
  getLatestSession: (id: string) =>
    api.get(`/topics/${id}/sessions/latest`, { suppressErrorToast: true } as OrkaAxiosConfig),
  getSubtopics: (id: string) => api.get(`/topics/${id}/subtopics`),
  getProgress: (id: string) => api.get(`/topics/${id}/progress`),
};
export const ChatAPI = {
  sendMessage: (data: {
    topicId?: string;
    sessionId?: string;
    content: string;
    isPlanMode?: boolean;
  }) => api.post("/chat/message", data),
  
  streamMessage: async (data: {
    topicId?: string;
    sessionId?: string;
    content: string;
    isPlanMode?: boolean;
  }) => {
    return authenticatedFetch("/api/chat/stream", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(data)
    });
  },

  endSession: (data: { sessionId: string }) =>
    api.post("/chat/session/end", data),
};

export const DashboardAPI = {
  getToday: () => api.get<DashboardTodayDto>("/dashboard/today"),
  previewAdaptiveStudyPlan: (data: AdaptiveStudyPlanRequestDto) =>
    api.post<AdaptiveStudyPlanDto>("/dashboard/adaptive-study-plan", data).then((r) => r.data),
  getStats: () => api.get("/dashboard/stats"),
  getRecentActivity: () => api.get("/dashboard/recent-activity"),
  getSystemHealth: () => api.get("/dashboard/system-health"),
  getDevDiagnostics: () => api.get("/dev/diagnostics/config"),
};

export interface DashboardTodayDto {
  dailyFocusTitle: string;
  dailyFocusReason: string;
  nextAction: {
    label: string;
    reason: string;
    view: string;
    topicId?: string | null;
    userSafeStatus: string;
  };
  weakConcepts: Array<{
    conceptKey: string;
    label: string;
    masteryProbability?: number | null;
    confidence?: number | null;
    topicId?: string | null;
    userSafeStatus: string;
    misconceptionSignal?: MisconceptionSignalDto | null;
    learningSignalConfidence?: LearningSignalConfidenceDto | null;
    remediationSeed?: RemediationSeedDto | null;
  }>;
  sourceHealth: {
    status: string;
    userSafeLabel: string;
    userSafeDetail: string;
    citationCoverage: number;
    unsupportedCitationCount: number;
    evidenceQuality?: EvidenceQualityDto | null;
  };
  dueReviewCount: number;
  activePlan?: {
    topicId: string;
    title: string;
    progressPercentage: number;
  } | null;
  coordinationScope?: {
    rootTopicId?: string | null;
    currentTopicId?: string | null;
    activeLessonTopicId?: string | null;
    treeTopicCount: number;
    sourceCount: number;
    quizAttemptCount: number;
    learningSignalCount: number;
  } | null;
  coordinationHealth?: {
    overallStatus: string;
    userSafeSummary: string;
    windowDays: number;
    rootTopicId?: string | null;
    currentTopicId?: string | null;
    activeLessonTopicId?: string | null;
    metrics: Array<{
      key: string;
      status: string;
      count: number;
      total: number;
      ratio: number;
      userSafeLabel: string;
      userSafeDetail: string;
    }>;
    generatedAt: string;
  } | null;
  recommendedEntryPoint: {
    view: string;
    label: string;
    reason: string;
  };
  learningMemory?: LearningMemoryLiteDto | null;
  adaptiveStudyPlan?: AdaptiveStudyPlanDto | null;
  longTermLearningProfile?: LongTermLearningProfileDto | null;
  examLearningProfile?: ExamLearningProfileDto | null;
  examWarRoom?: OrkaExamWarRoomDto | null;
  sourceWikiIntelligenceProfile?: SourceWikiIntelligenceProfileDto | null;
  sourceWikiPro?: OrkaSourceWikiProDto | null;
  orkaLearningState?: OrkaLearningStateDto | null;
  missionControl?: OrkaMissionControlDto | null;
  studyCoach?: OrkaStudyCoachDto | null;
  studyRoom?: OrkaStudyRoomDto | null;
  notebookStudioPro?: OrkaNotebookStudioProDto | null;
  codeLearningIde?: OrkaCodeLearningIdeDto | null;
  hasRealLearningData: boolean;
  generatedAt: string;
}

export const WikiAPI = {
  getTopicPages: (topicId: string) => api.get(`/wiki/${topicId}`),
  getPage: (pageId: string) => api.get(`/wiki/page/${pageId}`),
  getPageCopilot: (pageId: string) =>
    api.get<WikiCopilotContextDto>(`/wiki/page/${pageId}/copilot`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getGraph: (topicId: string) =>
    api.get<WikiGraphDto>(`/wiki/${topicId}/graph`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getPageGraph: (pageId: string) =>
    api.get<WikiGraphDto>(`/wiki/page/${pageId}/graph`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  syncGraph: (topicId: string, data: WikiGraphSyncRequestDto = {}) =>
    api.post<WikiGraphSyncResultDto>(`/wiki/${topicId}/sync-graph`, data).then((r) => r.data),
  getWorkspaceState: (topicId: string) =>
    api.get<{
      topicId: string;
      topicTitle: string;
      wikiPageCount: number;
      wikiBlockCount: number;
      sourceCount: number;
      readySourceCount: number;
      citationHealth: string;
      ragQualityStatus: string;
      retrievalHealth: string;
      citationCoverage: number;
      unsupportedCitationCount: number;
      activeConcepts: string[];
      weakConcepts: string[];
      recommendedActions: string[];
      generatedAt: string;
    }>(`/wiki/${topicId}/workspace-state`).then((r) => r.data),
  getKnowledgeNotebook: (topicId: string) =>
    api.get<WikiKnowledgeNotebookDto>(`/wiki/${topicId}/knowledge-notebook`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  refreshKnowledgeNotebook: (topicId: string) =>
    api.post<WikiKnowledgeNotebookDto>(`/wiki/${topicId}/knowledge-notebook/refresh`).then((r) => r.data),
  addBlock: (pageId: string, data: CreateWikiBlockRequestDto) =>
    api.post<WikiBlockDto>(`/wiki/page/${pageId}/blocks`, data).then((r) => r.data),
  addNote: (pageId: string, data: { content: string }) =>
    api.post(`/wiki/page/${pageId}/note`, data),
  updateBlock: (blockId: string, data: { content: string }) =>
    api.put(`/wiki/block/${blockId}`, data),
  deleteBlock: (blockId: string) => api.delete(`/wiki/block/${blockId}`),
  exportWiki: (topicId: string) => api.get(`/wiki/${topicId}/export`),
  /**
   * NotebookLM-tarzı Briefing Document.
   * 1 saatlik backend cache'i vardır — peş peşe çağrılarda cache'ten döner.
   */
  getBriefing: (topicId: string) =>
    api.get<{
      topicId: string;
      topicTitle: string;
      tldr: string;
      keyTakeaways: string[];
      suggestedQuestions: string[];
      generatedAt: string;
    }>(`/wiki/${topicId}/briefing`).then((r) => r.data),
  getGlossary: (topicId: string) =>
    api.get<{
      topicId: string;
      items: Array<{ term: string; simpleExplanation: string }>;
      generatedAt: string;
    }>(`/wiki/${topicId}/glossary`).then((r) => r.data),
  getTimeline: (topicId: string) =>
    api.get<{
      topicId: string;
      items: Array<{ year: string; event: string }>;
      generatedAt: string;
    }>(`/wiki/${topicId}/timeline`).then((r) => r.data),
  getMindMap: (topicId: string) =>
    api.get<{
      topicId: string;
      mermaid: string;
      nodes: Array<{ id: string; label: string; parentId?: string | null; depth: number }>;
      generatedAt: string;
    }>(`/wiki/${topicId}/mindmap`).then((r) => r.data),
  getStudyCards: (topicId: string) =>
    api.get<{
      topicId: string;
      cards: Array<{ front: string; back: string; sourceHint?: string }>;
      generatedAt: string;
    }>(`/wiki/${topicId}/study-cards`).then((r) => r.data),
  getRecommendations: (topicId: string) =>
    api.get<{
      topicId: string;
      items: Array<{
        id: string;
        recommendationType: string;
        title: string;
        reason: string;
        skillTag?: string;
        actionPrompt?: string;
        isDone: boolean;
        createdAt: string;
      }>;
      generatedAt: string;
    }>(`/wiki/${topicId}/recommendations`).then((r) => r.data.items ?? []),
};

export const SourcesAPI = {
  upload: (data: { topicId?: string; sessionId?: string; file: File }) => {
    const form = new FormData();
    if (data.topicId) form.append("topicId", data.topicId);
    if (data.sessionId) form.append("sessionId", data.sessionId);
    form.append("file", data.file);
    return api.post<{
      id: string;
      topicId?: string;
      sessionId?: string;
      sourceType: string;
      title: string;
      fileName: string;
      pageCount: number;
      chunkCount: number;
      status: string;
      createdAt: string;
    }>("/sources/upload", form, {
      headers: { "Content-Type": "multipart/form-data" },
    }).then((r) => r.data);
  },
  getTopicSources: (topicId: string) =>
    api.get<Array<{
      id: string;
      topicId?: string;
      sessionId?: string;
      sourceType: string;
      title: string;
      fileName: string;
      pageCount: number;
      chunkCount: number;
      status: string;
      createdAt: string;
    }>>(`/sources/topic/${topicId}`).then((r) => r.data),
  getTopicNotebook: (topicId: string) =>
    api.get<SourceNotebookDto>(`/sources/topic/${topicId}/notebook`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getSourceNotebook: (sourceId: string) =>
    api.get<SourceNotebookDto>(`/sources/${sourceId}/notebook`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getSourceConceptLinks: (sourceId: string) =>
    api.get<SourceConceptLinkSummaryDto>(`/sources/${sourceId}/concept-links`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  syncSourceConceptLinks: (sourceId: string) =>
    api.post<SourceConceptLinkSummaryDto>(`/sources/${sourceId}/concept-links/sync`, {}).then((r) => r.data),
  getTopicSourceConceptGraph: (topicId: string) =>
    api.get<SourceConceptGraphDto>(`/sources/topic/${topicId}/concept-graph`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getWikiPageSourceLinks: (pageId: string) =>
    api.get<SourceConceptLinkSummaryDto>(`/wiki/pages/${pageId}/source-links`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getTopicQuality: (topicId: string) =>
    api.get<SourceQualityReportDto>(`/sources/topic/${topicId}/quality`).then((r) => r.data),
  getEvidenceBundle: (topicId: string, sessionId?: string) =>
    api.get<SourceEvidenceBundleDto>(`/sources/topic/${topicId}/evidence-bundle`, {
      params: sessionId ? { sessionId } : undefined,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  refreshEvidenceBundle: (topicId: string, data?: SourceEvidenceBundleRequestDto) =>
    api.post<SourceEvidenceBundleDto>(`/sources/topic/${topicId}/evidence-bundle/refresh`, data ?? {}).then((r) => r.data),
  getLifecycleSummary: (topicId: string) =>
    api.get<SourceLifecycleSummaryDto>(`/sources/topic/${topicId}/lifecycle-summary`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  markStale: (sourceId: string, data?: MarkSourceStaleRequestDto) =>
    api.post(`/sources/${sourceId}/stale`, data ?? {}).then((r) => r.data),
  invalidateEvidence: (sourceId: string, data?: MarkSourceStaleRequestDto) =>
    api.post(`/sources/${sourceId}/invalidate-evidence`, data ?? {}).then((r) => r.data),
  validateCitations: (data: ValidateSourceCitationSetRequestDto) =>
    api.post<SourceCitationSetValidationDto>("/sources/citations/validate", data).then((r) => r.data),
  compare: (data: MultiSourceCompareRequestDto) =>
    api.post<MultiSourceCompareResultDto>("/sources/compare", data).then((r) => r.data),
  compareTopicSources: (topicId: string, data: MultiSourceCompareRequestDto) =>
    api.post<MultiSourceCompareResultDto>(`/sources/topic/${topicId}/compare`, {
      ...data,
      topicId,
    }).then((r) => r.data),
  getSourceCitationReview: (sourceId: string) =>
    api.get<CitationReviewResultDto>(`/sources/${sourceId}/citation-review`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getTopicCitationReview: (topicId: string) =>
    api.get<CitationReviewResultDto>(`/sources/topic/${topicId}/citation-review`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getSourceStudySummary: (params?: { topicId?: string; sourceId?: string; wikiPageId?: string }) =>
    api.get<SourceStudySummaryDto>("/sources/study-summary", {
      params,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  listQuestionThreads: (params?: { topicId?: string; sourceId?: string; wikiPageId?: string }) =>
    api.get<SourceQuestionThreadListDto>("/sources/question-threads", {
      params,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  getQuestionThread: (threadId: string) =>
    api.get<SourceQuestionThreadDto>(`/sources/question-threads/${threadId}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  createQuestionThread: (data: SourceQuestionThreadRequestDto) =>
    api.post<SourceQuestionThreadDto>("/sources/question-threads", data).then((r) => r.data),
  askQuestionThread: (threadId: string, data: SourceQuestionFollowUpRequestDto) =>
    api.post<SourceQuestionThreadDto>(`/sources/question-threads/${threadId}/ask`, data).then((r) => r.data),
  reviewQuestionThread: (threadId: string, data: SourceQuestionReviewStateDto) =>
    api.patch<SourceQuestionThreadDto>(`/sources/question-threads/${threadId}/review`, data).then((r) => r.data),
  writeQuestionThreadWikiTrace: (threadId: string) =>
    api.post<WikiBlockDto>(`/sources/question-threads/${threadId}/wiki-trace`, {}).then((r) => r.data),
  ask: (sourceId: string, question: string, data?: Partial<SourceQuestionRequestDto>) =>
    api.post<SourceQuestionResponseDto>(`/sources/${sourceId}/ask`, {
      ...(data ?? {}),
      sourceId,
      question,
      mode: data?.mode ?? "selected_source",
    }).then((r) => r.data),
  askTopicSources: (topicId: string, question: string, data?: Partial<SourceQuestionRequestDto>) =>
    api.post<SourceQuestionResponseDto>(`/sources/topic/${topicId}/ask`, {
      ...(data ?? {}),
      topicId,
      question,
      mode: data?.mode ?? "source_collection",
    }).then((r) => r.data),
  askSources: (data: SourceQuestionRequestDto) =>
    api.post<SourceQuestionResponseDto>("/sources/ask", data).then((r) => r.data),
  getPage: (sourceId: string, page: number) =>
    api.get<{
      sourceId: string;
      pageNumber: number;
      title: string;
      chunks: Array<{
        id: string;
        pageNumber: number;
        chunkIndex: number;
        text: string;
        highlightHint?: string;
      }>;
    }>(`/sources/${sourceId}/pages/${page}`).then((r) => r.data),
  update: (sourceId: string, data: { title?: string; fileName?: string }) =>
    api.patch(`/sources/${sourceId}`, data).then((r) => r.data),
  delete: (sourceId: string) =>
    api.delete(`/sources/${sourceId}`).then((r) => r.data),
  getWikiPro: (params: OrkaLearningContractQuery) =>
    api.get<OrkaSourceWikiProDto>("/sources/wiki-pro", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
};

export const AudioOverviewAPI = {
  create: (data: { topicId?: string; sessionId?: string }) =>
    api.post<{
      id: string;
      status: string;
      script: string;
      speakers: string[];
      errorMessage?: string;
      createdAt: string;
    }>("/audio/overview", data).then((r) => r.data),
  get: (jobId: string) =>
    api.get<{
      id: string;
      status: string;
      script: string;
      speakers: string[];
      errorMessage?: string;
      createdAt: string;
    }>(`/audio/overview/${jobId}`).then((r) => r.data),
  streamUrl: (jobId: string) => buildApiUrl(`/api/audio/overview/${jobId}/stream`),
  fetchBlob: async (jobId: string): Promise<string> => {
    const resp = await api.get(`/audio/overview/${jobId}/stream`, { responseType: "blob" });
    return URL.createObjectURL(resp.data as Blob);
  },
};

export const LearningAPI = {
  recordSignal: (data: {
    topicId?: string;
    sessionId?: string;
    signalType: string;
    skillTag?: string;
    topicPath?: string;
    score?: number;
    isPositive?: boolean;
    payloadJson?: string;
  }) => api.post("/learning/signal", data, { suppressErrorToast: true } as OrkaAxiosConfig),
  getTopicSummary: (topicId: string) =>
    api.get<{
      topicId: string;
      totalAttempts: number;
      correctAttempts: number;
      accuracy: number;
      weakSkills: Array<{
        skillTag: string;
        topicPath: string;
        wrongCount: number;
        totalCount: number;
        accuracy: number;
        lastSeenAt: string;
      }>;
      recentSignals: string[];
      cache?: {
        hit: boolean;
        source: string;
        generatedAt: string;
        cachedAt?: string | null;
        version?: number | null;
      } | null;
    }>(`/learning/topic/${topicId}/summary`).then((r) => r.data),
  getTopicQuality: (topicId: string) =>
    api.get<LearningQualityReport>(`/learning-quality/topic/${topicId}`).then((r) => r.data),
  runRagEvaluation: (topicId: string) =>
    api.post(`/learning-quality/topic/${topicId}/rag-evaluation/run`).then((r) => r.data),
  getMissionControl: (params: OrkaLearningContractQuery) =>
    api.get<OrkaMissionControlDto>("/learning/mission-control", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getStudyCoach: (params: OrkaLearningContractQuery) =>
    api.get<OrkaStudyCoachDto>("/learning/study-coach", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getOrkaState: (params: OrkaLearningContractQuery) =>
    api.get<OrkaLearningStateDto>("/learning/orka-state", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
};

export const LearningSnapshotsAPI = {
  getActiveLesson: (params: { topicId?: string; sessionId?: string }) =>
    api.get<ActiveLessonSnapshotDto>("/learning-snapshots/active-lesson", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  refreshActiveLesson: (data: ActiveLessonSnapshotRequestDto) =>
    api.post<ActiveLessonSnapshotDto>("/learning-snapshots/active-lesson/refresh", data).then((r) => r.data),
  markActiveLessonStale: (data: ActiveLessonSnapshotRequestDto, reason = "context_changed") =>
    api.post("/learning-snapshots/active-lesson/stale", data, { params: { reason } }).then((r) => r.data),
  getStudentContext: (params: { topicId?: string; sessionId?: string }) =>
    api.get<StudentContextSnapshotDto>("/learning-snapshots/student-context", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  refreshStudentContext: (data: StudentContextSnapshotRequestDto) =>
    api.post<StudentContextSnapshotDto>("/learning-snapshots/student-context/refresh", data).then((r) => r.data),
};

export const PlanQualityAPI = {
  getReadiness: (topicId: string, sessionId?: string) =>
    api.get<PlanReadinessDto>(`/plan-quality/topic/${topicId}/readiness`, {
      params: sessionId ? { sessionId } : undefined,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  evaluate: (data: PlanQualityEvaluationRequestDto) =>
    api.post<PlanQualityEvaluationDto>("/plan-quality/evaluate", data).then((r) => r.data),
  getSnapshot: (id: string) =>
    api.get<PlanQualityEvaluationDto>(`/plan-quality/snapshots/${id}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getLatest: (topicId: string, sessionId?: string) =>
    api.get<PlanQualityEvaluationDto>(`/plan-quality/topic/${topicId}/latest`, {
      params: sessionId ? { sessionId } : undefined,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
};

export const TutorAPI = {
  getTopicState: (topicId: string) =>
    api.get(`/tutor/state/topic/${topicId}`).then((r) => r.data),
  getTrace: (traceId: string) =>
    api.get(`/tutor/trace/${traceId}`).then((r) => r.data),
  getPedagogyTopic: (topicId: string) =>
    api.get(`/tutor/pedagogy/topic/${topicId}`).then((r) => r.data),
  getPedagogyRun: (runId: string) =>
    api.get(`/tutor/pedagogy/run/${runId}`).then((r) => r.data),
  getSessionPolicy: (sessionId: string) =>
    api.get<TutorResponsePolicyDto>(`/tutor/policy/session/${sessionId}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getTopicPolicy: (topicId: string, sessionId?: string) =>
    api.get<TutorResponsePolicyDto>(`/tutor/policy/topic/${topicId}`, {
      params: sessionId ? { sessionId } : undefined,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  evaluateResponsePolicy: (data: TutorResponseQualityEvaluationRequestDto) =>
    api.post<TutorResponseQualityEvaluationDto>("/tutor/policy/evaluate", data).then((r) => r.data),
  getLatestResponseQuality: (data: { topicId?: string; sessionId?: string } = {}) =>
    api.get<TutorResponseQualityEvaluationDto | null>("/tutor/response-quality/latest", {
      params: data,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  getNextActions: (data: TutorResponsePolicyRequestDto = {}) =>
    api.get<TutorNextLearningActionDto[]>("/tutor/next-actions", {
      params: data,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  evaluateRecentPedagogy: (data: { topicId?: string; sessionId?: string }) => {
    const params = new URLSearchParams();
    if (data.topicId) params.set("topicId", data.topicId);
    if (data.sessionId) params.set("sessionId", data.sessionId);
    const query = params.toString();
    return api.post(`/tutor/pedagogy/evaluate/recent${query ? `?${query}` : ""}`).then((r) => r.data);
  },
  getArtifact: (artifactId: string) =>
    api.get<TeachingArtifact>(`/tutor/artifacts/${artifactId}`).then((r) => r.data),
  markArtifactRendered: (artifactId: string, renderError?: string | null) =>
    api.post(`/tutor/artifacts/${artifactId}/rendered`, { renderError: renderError ?? null }, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getSessionEvents: (sessionId: string, after = "0-0", take = 50) =>
    api.get(`/tutor/events/session/${sessionId}?after=${encodeURIComponent(after)}&take=${take}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getSessionTimeline: (sessionId: string, after = "0-0", take = 50) =>
    api.get<TutorTraceTimeline>(`/tutor/events/session/${sessionId}/timeline?after=${encodeURIComponent(after)}&take=${take}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  recordStyleSignal: (data: { topicId?: string; sessionId?: string; message: string }) =>
    api.post("/tutor/style-signal", data).then((r) => r.data),
};

export const LearningArtifactsAPI = {
  get: (artifactId: string) =>
    api.get<LearningArtifactDto>(`/learning-artifacts/${artifactId}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  list: (data: { topicId?: string; sessionId?: string; conceptKey?: string } = {}) =>
    api.get<LearningArtifactListDto>("/learning-artifacts", {
      params: data,
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  create: (data: LearningArtifactRequestDto) =>
    api.post<LearningArtifactDto>("/learning-artifacts", data).then((r) => r.data),
  validate: (data: LearningArtifactRequestDto) =>
    api.post<LearningArtifactSafetyDto>("/learning-artifacts/validate", data, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  refreshStatus: (artifactId: string, data: LearningArtifactRefreshRequestDto = {}) =>
    api.post<LearningArtifactDto>(`/learning-artifacts/${artifactId}/refresh-status`, data, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
};

export const NotebookStudioAPI = {
  listPacks: (topicId: string, sessionId?: string, wikiPageId?: string, options: { surface?: string; sourceId?: string } = {}) =>
    api.get<LearningNotebookPackListDto>(`/notebook-studio/topic/${topicId}/packs`, {
      params: {
        ...(sessionId ? { sessionId } : {}),
        ...(wikiPageId ? { wikiPageId } : {}),
        ...(options.surface ? { surface: options.surface } : {}),
        ...(options.sourceId ? { sourceId: options.sourceId } : {}),
      },
      suppressErrorToast: true,
    } as OrkaAxiosConfig).then((r) => r.data),
  getPack: (packId: string) =>
    api.get<LearningNotebookPackDto>(`/notebook-studio/packs/${packId}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  buildMilestonePack: (topicId: string, data: LearningNotebookPackRequestDto = {}) =>
    api.post<LearningNotebookPackDto>(`/notebook-studio/topic/${topicId}/milestone-pack`, data).then((r) => r.data),
  buildWikiPagePack: (pageId: string, data: LearningNotebookPackRequestDto = {}) =>
    api.post<LearningNotebookPackDto>(`/notebook-studio/wiki-page/${pageId}/pack`, data).then((r) => r.data),
  buildSourcePack: (sourceId: string, data: LearningNotebookPackRequestDto = {}) =>
    api.post<LearningNotebookPackDto>(`/notebook-studio/sources/${sourceId}/pack`, data).then((r) => r.data),
  buildTopicSourcePack: (topicId: string, data: LearningNotebookPackRequestDto = {}) =>
    api.post<LearningNotebookPackDto>(`/notebook-studio/topic/${topicId}/source-pack`, data).then((r) => r.data),
  refreshPack: (packId: string) =>
    api.post<LearningNotebookPackDto>(`/notebook-studio/packs/${packId}/refresh`, {}, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  buildArtifact: (packId: string, data: LearningNotebookArtifactRequestDto) =>
    api.post<LearningArtifactDto>(`/notebook-studio/packs/${packId}/artifact`, data).then((r) => r.data),
  getExportPreview: (packId: string) =>
    api.get<NotebookSlideExportPreviewDto>(`/notebook-studio/packs/${packId}/export/preview`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  exportPack: (packId: string, data: NotebookExportRequestDto = { format: "markdown" }) =>
    api.post<NotebookExportResultDto>(`/notebook-studio/packs/${packId}/export`, data, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getPro: (params: OrkaLearningContractQuery) =>
    api.get<OrkaNotebookStudioProDto>("/notebook-studio/pro", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
};

export const AssessmentAPI = {
  getCalibration: (topicId: string) =>
    api.get<AssessmentCalibrationRun>(`/assessment/topic/${topicId}/calibration`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  runCalibration: (topicId: string) =>
    api.post<AssessmentCalibrationRun>(`/assessment/topic/${topicId}/calibration/run`).then((r) => r.data),
  getTopicBlueprint: (topicId: string, sessionId?: string) =>
    api.get<AssessmentBlueprintDto>(
      `/assessment/topic/${topicId}/blueprint${sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : ""}`,
      { suppressErrorToast: true } as OrkaAxiosConfig
    ).then((r) => r.data),
  buildPlanStepBlueprint: (data: AssessmentBlueprintRequestDto) =>
    api.post<AssessmentBlueprintDto>("/assessment/blueprint/plan-step", data).then((r) => r.data),
  buildDiagnosticBlueprint: (data: AssessmentBlueprintRequestDto) =>
    api.post<AssessmentBlueprintDto>("/assessment/blueprint/diagnostic", data).then((r) => r.data),
  evaluateQuality: (data: AssessmentQualityEvaluationRequestDto) =>
    api.post<AssessmentQualityEvaluationDto>("/assessment/quality/evaluate", data).then((r) => r.data),
  getQualitySnapshot: (snapshotId: string) =>
    api.get<AssessmentQualityEvaluationDto>(`/assessment/quality/snapshots/${snapshotId}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getLatestQuality: (topicId: string, sessionId?: string) =>
    api.get<AssessmentQualityEvaluationDto | null>(
      `/assessment/topic/${topicId}/quality/latest${sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : ""}`,
      { suppressErrorToast: true } as OrkaAxiosConfig
    ).then((r) => r.data),
};

export const ClassroomAPI = {
  start: (data: { topicId?: string; sessionId?: string; audioOverviewJobId?: string; transcript: string }) =>
    api.post<{
      id: string;
      topicId?: string;
      sessionId?: string;
      audioOverviewJobId?: string;
      status: string;
      createdAt: string;
      updatedAt: string;
    }>("/classroom/session", data).then((r) => r.data),
  ask: (id: string, data: { question: string; activeSegment?: string }) =>
    api.post<{
      classroomSessionId: string;
      interactionId?: string;
      answer: string;
      speakers: string[];
    }>(`/classroom/${id}/ask`, data).then((r) => r.data),
  getInteractionAudio: (interactionId: string) =>
    api.get<Blob>(`/classroom/interaction/${interactionId}/audio`, {
      responseType: "blob",
      suppressErrorToast: true,
    } as OrkaAxiosConfig),
  getStudyRoom: (params: OrkaLearningContractQuery) =>
    api.get<OrkaStudyRoomDto>("/classroom/study-room", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  startStudyRoom: (request: OrkaStudyRoomStartRequestDto) =>
    api.post<OrkaStudyRoomDto>("/classroom/study-room/start", request).then((r) => r.data),
  submitStudyRoomCheckpoint: (request: OrkaStudyRoomCheckpointRequestDto) =>
    api.post<OrkaStudyRoomDto>("/classroom/study-room/checkpoint", request).then((r) => r.data),
};

/**
 * QuizAPI — Quiz denemelerini backend'e kaydeder.
 * POST /api/quiz/attempt endpoint'i backend'de hazır olmadığında
 * sessizce başarısız olur (fire-and-forget).
 */
type QuizAttemptPayload = {
  messageId: string;
  quizRunId?: string;
  questionId?: string;
  topicId?: string;
  sessionId?: string;
  question: string;
  selectedOptionId: string;
  isCorrect?: boolean;
  explanation?: string;
  skillTag?: string;
  assessmentItemId?: string;
  conceptKey?: string;
  conceptTag?: string;
  cognitiveSkill?: string;
  misconceptionTarget?: string;
  evidenceExpected?: string;
  scoringRule?: string;
  learningOutcomeIdsJson?: string;
  topicPath?: string;
  difficulty?: string;
  cognitiveType?: string;
  questionHash?: string;
  sourceRefsJson?: string;
  responseTimeMs?: number;
  wasSkipped?: boolean;
  confidenceSelfRating?: number;
  assessmentMode?: string;
  sourceEvidenceBundleId?: string;
  wikiNotebookSectionKey?: string;
};

export type QuizAttemptRecordResponse = {
  id: string;
  quizRunId?: string | null;
  topicId?: string | null;
  skillTag?: string | null;
  questionHash?: string | null;
  knowledgeTracingStateId?: string | null;
  masteryProbability?: number | null;
  itemQualityStatus?: string | null;
  xp?: unknown;
  review?: unknown;
  mistake?: unknown;
  misconceptionSignal?: MisconceptionSignalDto | null;
  learningSignalConfidence?: LearningSignalConfidenceDto | null;
  remediationSeed?: RemediationSeedDto | null;
  learningImpact?: import("@/lib/types").QuizResultLearningImpactDto | null;
};

export const QuizAPI = {
  analyzePlanIntent: (data: {
    rawRequest: string;
    topicId?: string;
    existingTopicTitle?: string;
    correction?: string;
  }) =>
    api.post<StudyIntentPreview>("/quiz/plan-diagnostic/intent", data).then((r) => r.data),
  startPlanDiagnostic: (data: {
    topicId: string;
    sessionId?: string;
    topicTitle?: string;
    userLevel?: string;
    intentRequestId?: string;
    rawStudyRequest?: string;
    approvedMainTopic?: string;
    approvedFocusArea?: string;
    approvedStudyGoal?: string;
    approvedResearchIntent?: string;
  }) =>
    api.post<{
      planRequestId: string;
      quizRunId: string;
      topicId: string;
      topicTitle: string;
      status: string;
      questionsJson: string;
      groundingMode: string;
      sourceCount: number;
      quizQuestionCount: number;
      conceptGraphQualityStatus?: string;
      assessmentQualityStatus?: string;
      qualityReportId?: string | null;
      intentRequestId?: string | null;
      approvedMainTopic: string;
      approvedFocusArea: string;
      approvedStudyGoal: string;
      approvedResearchIntent: string;
    }>("/quiz/plan-diagnostic/start", data).then((r) => r.data),
  recordPlanDiagnosticAttempt: (planRequestId: string, data: QuizAttemptPayload) =>
    api.post<{
      planRequestId: string;
      quizRunId: string;
      status: string;
      answeredQuestionCount: number;
      quizQuestionCount: number;
      learningImpact?: import("@/lib/types").QuizResultLearningImpactDto | null;
    }>(`/quiz/plan-diagnostic/${planRequestId}/attempt`, data, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  finalizePlanDiagnostic: (planRequestId: string) =>
    api.post<{
      planRequestId: string;
      status: string;
      planGenerated: boolean;
      message?: string;
      generatedPlanRootTopicId?: string;
      generatedTopicIds: string[];
    }>("/quiz/plan-diagnostic/finalize", { planRequestId }).then((r) => r.data),
  skipPlanDiagnostic: (planRequestId: string) =>
    api.post<{
      planRequestId: string;
      status: string;
      planGenerated: boolean;
      message?: string;
      generatedPlanRootTopicId?: string;
      generatedTopicIds: string[];
    }>(`/quiz/plan-diagnostic/${planRequestId}/skip`, {}, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  recordAttempt: (data: QuizAttemptPayload) =>
    api.post<QuizAttemptRecordResponse>("/quiz/attempt", data, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  startAdaptive: (data: { topicId?: string; sessionId?: string; minItems?: number; maxItems?: number; targetConceptKeys?: string[]; assessmentMode?: string }) =>
    api.post<AdaptiveAssessmentSession>("/quiz/adaptive/start", data).then((r) => r.data),
  getAdaptiveNext: (adaptiveSessionId: string) =>
    api.get<AdaptiveAssessmentNextItem>(`/quiz/adaptive/${adaptiveSessionId}/next`).then((r) => r.data),
  answerAdaptive: (adaptiveSessionId: string, data: QuizAttemptPayload & { decisionId: string }) =>
    api.post<AdaptiveAssessmentNextItem>(`/quiz/adaptive/${adaptiveSessionId}/answer`, data, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
  getGlobalStats: () => api.get("/quiz/stats"),
  getHistory: (topicId: string) => api.get(`/quiz/history/${topicId}`),
};

export interface KorteksSyncResponseDto {
  success: boolean;
  topic?: string;
  report?: string;
  answer?: string;
  research?: string;
  groundingMode?: string;
  sourceCount?: number;
  sources?: Array<{
    provider?: string;
    title?: string | null;
    url?: string | null;
    citationLabel?: string | null;
    confidence?: number | null;
  }>;
  providerWarnings?: string[];
  providerCalls?: unknown[];
  isFallback?: boolean;
  legacySources?: string[];
  synthesisWorkflowId?: string;
  synthesisStatus?: string;
  synthesis?: KorteksResearchSynthesis;
  consumerContexts?: KorteksConsumerContexts;
  evidenceSummary?: KorteksEvidenceSummary;
  safetyIssues?: KorteksSynthesisIssue[];
}

export const KorteksAPI = {
  researchSync: (data: { topic: string; topicId?: string; sourceUrl?: string }) =>
    api.post<KorteksSyncResponseDto>("/korteks/research", data).then((r) => r.data),

  getSynthesis: (id: string) =>
    api.get<KorteksResearchWorkflow>(`/korteks/synthesis/${id}`, { suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),

  getLatestSynthesis: (params?: { topicId?: string; sessionId?: string }) => {
    const search = new URLSearchParams();
    if (params?.topicId) search.set("topicId", params.topicId);
    if (params?.sessionId) search.set("sessionId", params.sessionId);
    const query = search.toString();
    return api
      .get<KorteksResearchWorkflow>(`/korteks/synthesis/latest${query ? `?${query}` : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },

  // Stream topic research.
  stream: (data: { topic: string; topicId?: string; sourceUrl?: string }) => {
    return authenticatedFetch("/api/korteks/research-stream", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(data),
    });
  },

  // Stream research with an uploaded PDF / TXT / MD file.
  streamWithFile: (data: { topic: string; topicId?: string; file: File }) => {
    const form = new FormData();
    form.append("topic", data.topic);
    if (data.topicId) form.append("topicId", data.topicId);
    form.append("file", data.file);
    return authenticatedFetch("/api/korteks/research-file", {
      method: "POST",
      body: form,
    });
  },
};

export const ToolsAPI = {
  getCapabilities: (includeInternal = false) =>
    api
      .get<ToolCapabilitiesResponse>(`/tools/capabilities${includeInternal ? "?includeInternal=true" : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  getCapability: (toolId: string, includeInternal = false) =>
    api
      .get<ToolCapability>(`/tools/capabilities/${toolId}${includeInternal ? "?includeInternal=true" : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  getRuntimeTraces: (params?: { topicId?: string; sessionId?: string; take?: number }) => {
    const search = new URLSearchParams();
    if (params?.topicId) search.set("topicId", params.topicId);
    if (params?.sessionId) search.set("sessionId", params.sessionId);
    if (params?.take) search.set("take", String(params.take));
    const query = search.toString();
    return api
      .get<ToolRuntimeTracesResponse>(`/tools/runtime/traces${query ? `?${query}` : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },
  getRuntimeTrace: (traceId: string) =>
    api
      .get<ToolRuntimeTrace>(`/tools/runtime/traces/${traceId}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  getGovernanceSummary: (params?: { topicId?: string; sessionId?: string }) => {
    const search = new URLSearchParams();
    if (params?.topicId) search.set("topicId", params.topicId);
    if (params?.sessionId) search.set("sessionId", params.sessionId);
    const query = search.toString();
    return api
      .get<ToolGovernanceSummary>(`/tools/runtime/governance-summary${query ? `?${query}` : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },
  decideRuntime: (data: ToolRuntimeRequest) =>
    api
      .post<ToolRuntimeDecision>("/tools/runtime/decide", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
};

export const LearningRuntimeAPI = {
  getTraces: (params?: { topicId?: string; sessionId?: string; take?: number }) => {
    const search = new URLSearchParams();
    if (params?.topicId) search.set("topicId", params.topicId);
    if (params?.sessionId) search.set("sessionId", params.sessionId);
    if (params?.take) search.set("take", String(params.take));
    const query = search.toString();
    return api
      .get<LearningRuntimeTracesResponseDto>(`/learning-runtime/traces${query ? `?${query}` : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },
  getTrace: (traceId: string) =>
    api
      .get<LearningRuntimeTraceDto>(`/learning-runtime/traces/${traceId}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  getCorrelation: (correlationId: string) =>
    api
      .get<LearningRuntimeCorrelationDto>(`/learning-runtime/correlation/${encodeURIComponent(correlationId)}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  getHealth: (params?: { topicId?: string; sessionId?: string }) => {
    const search = new URLSearchParams();
    if (params?.topicId) search.set("topicId", params.topicId);
    if (params?.sessionId) search.set("sessionId", params.sessionId);
    const query = search.toString();
    return api
      .get<LearningRuntimeHealthDto>(`/learning-runtime/health${query ? `?${query}` : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },
  getTopicSummary: (topicId: string, sessionId?: string) => {
    const search = new URLSearchParams();
    if (sessionId) search.set("sessionId", sessionId);
    const query = search.toString();
    return api
      .get<LearningRuntimeFlowSummaryDto>(`/learning-runtime/topic/${topicId}/summary${query ? `?${query}` : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },
  privacyCheck: (data: LearningRuntimePrivacyCheckRequestDto) =>
    api
      .post<LearningRuntimePrivacyCheckDto>("/learning-runtime/privacy-check", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
};

export const AgenticTrustAPI = {
  checkUserMessage: (data: AgenticTrustCheckRequestDto) =>
    api
      .post<AgenticTrustCheckResultDto>("/agentic-trust/check/user-message", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  checkSourceContent: (data: AgenticTrustCheckRequestDto) =>
    api
      .post<AgenticTrustCheckResultDto>("/agentic-trust/check/source-content", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  checkToolRequest: (data: AgenticTrustCheckRequestDto) =>
    api
      .post<AgenticTrustCheckResultDto>("/agentic-trust/check/tool-request", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  checkTutorResponse: (data: AgenticTrustCheckRequestDto) =>
    api
      .post<AgenticTrustCheckResultDto>("/agentic-trust/check/tutor-response", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  checkMemoryWrite: (data: AgenticTrustCheckRequestDto) =>
    api
      .post<AgenticTrustCheckResultDto>("/agentic-trust/check/memory-write", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  checkCitationSet: (data: AgenticTrustCheckRequestDto) =>
    api
      .post<AgenticTrustCheckResultDto>("/agentic-trust/check/citation-set", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  checkPublicPayload: (data: AgenticTrustCheckRequestDto) =>
    api
      .post<AgenticTrustCheckResultDto>("/agentic-trust/check/public-payload", data, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data),
  getSummary: (topicId?: string, sessionId?: string) => {
    const params = new URLSearchParams();
    if (topicId) params.set("topicId", topicId);
    if (sessionId) params.set("sessionId", sessionId);
    const query = params.toString();
    return api
      .get<AgenticTrustRuntimeSummaryDto>(`/agentic-trust/summary${query ? `?${query}` : ""}`, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },
  evaluateFixtures: (topicId?: string, sessionId?: string) => {
    const params = new URLSearchParams();
    if (topicId) params.set("topicId", topicId);
    if (sessionId) params.set("sessionId", sessionId);
    const query = params.toString();
    return api
      .post<AgenticTrustRuntimeSummaryDto>(`/agentic-trust/fixtures/evaluate${query ? `?${query}` : ""}`, null, { suppressErrorToast: true } as OrkaAxiosConfig)
      .then((r) => r.data);
  },
};

export const StandardsAPI = {
  getSummary: (topicId: string) =>
    api.get<StandardsSummary>(`/standards/topic/${topicId}/summary`).then((r) => r.data),
  validate: (topicId: string) =>
    api.post<StandardsValidationRun>(`/standards/topic/${topicId}/validate`).then((r) => r.data),
  exportTopic: (topicId: string, exportType = "combined") =>
    api.post<StandardsExportRun>(`/standards/topic/${topicId}/export?exportType=${encodeURIComponent(exportType)}`).then((r) => r.data),
};

export const ExamsAPI = {
  getDefinitions: () => api.get<ExamDefinitionDto[]>("/exams").then((r) => r.data),
  getTree: (examCode: string) => api.get<ExamDefinitionDto>(`/exams/${encodeURIComponent(examCode)}`).then((r) => r.data),
  getVariantTree: (examCode: string, variantCode: string) =>
    api.get<ExamDefinitionDto>(`/exams/${encodeURIComponent(examCode)}/variants/${encodeURIComponent(variantCode)}`).then((r) => r.data),
  importTree: (data: ExamTreeImportDto) => api.post<ExamDefinitionDto>("/exams/import-tree", data).then((r) => r.data),
};

export const QuestionsAPI = {
  getQuestions: (filters: QuestionBankFilterDto = {}) =>
    api.get<QuestionItemDto[]>("/questions", { params: filters }).then((r) => r.data),
  getQuestion: (id: string) => api.get<QuestionItemDto>(`/questions/${id}`).then((r) => r.data),
  createQuestion: (data: CreateQuestionDto) => api.post<QuestionItemDto>("/questions", data).then((r) => r.data),
  updateQuestion: (id: string, data: UpdateQuestionDto) => api.put<QuestionItemDto>(`/questions/${id}`, data).then((r) => r.data),
  submitForReview: (id: string) => api.post<QuestionItemDto>(`/questions/${id}/submit-review`).then((r) => r.data),
  publishQuestion: (id: string) => api.post<QuestionItemDto>(`/questions/${id}/publish`).then((r) => r.data),
  deleteQuestion: (id: string) => api.delete(`/questions/${id}`).then((r) => r.data),
  addContentBlock: (id: string, data: CreateQuestionContentBlockDto) =>
    api.post<QuestionItemDto>(`/questions/${id}/content-blocks`, data).then((r) => r.data),
  createStimulus: (data: CreateQuestionStimulusDto) =>
    api.post<QuestionStimulusDto>("/questions/stimuli", data).then((r) => r.data),
  attachStimulus: (id: string, data: QuestionStimulusLinkDto) =>
    api.post<QuestionItemDto>(`/questions/${id}/stimuli`, data).then((r) => r.data),
  addOptionContentBlock: (optionId: string, data: CreateQuestionOptionContentBlockDto) =>
    api.post<QuestionItemDto>(`/questions/options/${optionId}/content-blocks`, data).then((r) => r.data),
  createAsset: (data: CreateQuestionAssetDto) =>
    api.post<QuestionAssetDto>("/question-assets", data).then((r) => r.data),
  getAsset: (id: string) =>
    api.get<QuestionAssetDto>(`/question-assets/${id}`).then((r) => r.data),
};

export const QuestionImportsAPI = {
  previewImport: (data: QuestionImportRequestDto) =>
    api.post<QuestionImportPreviewDto>("/question-imports/preview", data).then((r) => r.data),
  previewPackage: (data: QuestionImportPackageDto) =>
    api.post<QuestionImportPreviewDto>("/question-imports/preview-package", data).then((r) => r.data),
  previewAiken: (data: QuestionImportTextAdapterRequestDto) =>
    api.post<QuestionImportPreviewDto>("/question-imports/preview-aiken", data).then((r) => r.data),
  previewGift: (data: QuestionImportTextAdapterRequestDto) =>
    api.post<QuestionImportPreviewDto>("/question-imports/preview-gift", data).then((r) => r.data),
  previewQti: (data: QuestionImportTextAdapterRequestDto) =>
    api.post<QuestionImportPreviewDto>("/question-imports/preview-qti", data).then((r) => r.data),
  previewMoodle: (data: QuestionImportTextAdapterRequestDto) =>
    api.post<QuestionImportPreviewDto>("/question-imports/preview-moodle", data).then((r) => r.data),
  approveImport: (data: QuestionImportApprovalDto) =>
    api.post<QuestionImportResultDto>("/question-imports/approve", data).then((r) => r.data),
  getPreview: (id: string) =>
    api.get<QuestionImportPreviewDto>(`/question-imports/${id}`).then((r) => r.data),
};

export const QuestionDraftsAPI = {
  previewDrafts: (data: QuestionDraftGenerationRequestDto) =>
    api.post<QuestionDraftPreviewDto>("/question-drafts/preview", data).then((r) => r.data),
  approveDrafts: (data: QuestionDraftApprovalDto) =>
    api.post<QuestionDraftApprovalResultDto>("/question-drafts/approve", data).then((r) => r.data),
  getPreview: (id: string) =>
    api.get<QuestionDraftPreviewDto>(`/question-drafts/${id}`).then((r) => r.data),
};

export const ContentOpsAPI = {
  getWorkflow: (questionId: string) =>
    api.get<QuestionReviewWorkflowDto>(`/content-ops/questions/${questionId}/workflow`).then((r) => r.data),
  submitReview: (questionId: string, data: SubmitQuestionReviewDto = {}) =>
    api.post<QuestionReviewWorkflowDto>(`/content-ops/questions/${questionId}/submit-review`, data).then((r) => r.data),
  assignReviewer: (questionId: string, data: AssignQuestionReviewerDto) =>
    api.post<QuestionReviewWorkflowDto>(`/content-ops/questions/${questionId}/assign-reviewer`, data).then((r) => r.data),
  advanceStage: (questionId: string, data: AdvanceQuestionReviewStageDto) =>
    api.post<QuestionReviewWorkflowDto>(`/content-ops/questions/${questionId}/advance-stage`, data).then((r) => r.data),
  reject: (questionId: string, data: RejectQuestionReviewDto) =>
    api.post<QuestionReviewWorkflowDto>(`/content-ops/questions/${questionId}/reject`, data).then((r) => r.data),
  retire: (questionId: string, data: RetireQuestionDto) =>
    api.post<QuestionReviewWorkflowDto>(`/content-ops/questions/${questionId}/retire`, data).then((r) => r.data),
  getPublishReadiness: (questionId: string) =>
    api.get<QuestionPublishReadinessDto>(`/content-ops/questions/${questionId}/publish-readiness`).then((r) => r.data),
  publish: (questionId: string, data: PublishQuestionContentDto = {}) =>
    api.post<QuestionItemDto>(`/content-ops/questions/${questionId}/publish`, data).then((r) => r.data),
  getVersions: (questionId: string) =>
    api.get<QuestionContentVersionDto[]>(`/content-ops/questions/${questionId}/versions`).then((r) => r.data),
};

export const CurriculumAPI = {
  getSources: () =>
    api.get<SourceRegistryItemDto[]>("/curriculum/sources").then((r) => r.data),
  getSource: (id: string) =>
    api.get<SourceRegistryItemDto>(`/curriculum/sources/${id}`).then((r) => r.data),
  registerSource: (data: RegisterSourceRegistryItemDto) =>
    api.post<SourceRegistryItemDto>("/curriculum/sources", data).then((r) => r.data),
  verifySource: (id: string, data: VerifySourceRegistryItemDto) =>
    api.post<SourceRegistryItemDto>(`/curriculum/sources/${id}/verify`, data).then((r) => r.data),
  reviewSourceLicense: (id: string, data: ReviewSourceLicenseDto) =>
    api.post<ContentLicenseReviewDto>(`/curriculum/sources/${id}/license-review`, data).then((r) => r.data),
    createVersion: (data: CreateCurriculumVersionDto) =>
      api.post<CurriculumVersionDto>("/curriculum/versions", data).then((r) => r.data),
    getVersion: (id: string) =>
      api.get<CurriculumVersionDto>(`/curriculum/versions/${id}`).then((r) => r.data),
    getExamVersions: (examCode: string) =>
      api.get<CurriculumVersionDto[]>(`/curriculum/exams/${examCode}/versions`).then((r) => r.data),
    deprecateVersion: (id: string, data: DeprecateCurriculumVersionDto = {}) =>
      api.post<CurriculumVersionDto>(`/curriculum/versions/${id}/deprecate`, data).then((r) => r.data),
    supersedeVersion: (id: string, data: SupersedeCurriculumVersionDto) =>
      api.post<CurriculumVersionDto>(`/curriculum/versions/${id}/supersede`, data).then((r) => r.data),
    addNode: (versionId: string, data: CreateCurriculumNodeDto) =>
      api.post<CurriculumNodeDto>(`/curriculum/versions/${versionId}/nodes`, data).then((r) => r.data),
  mapOutcome: (versionId: string, data: CreateCurriculumOutcomeMappingDto) =>
    api.post<CurriculumOutcomeMappingDto>(`/curriculum/versions/${versionId}/outcome-mappings`, data).then((r) => r.data),
  getOutcomeSources: (examOutcomeId: string) =>
    api.get<CurriculumOutcomeSourceDto>(`/curriculum/outcomes/${examOutcomeId}/sources`).then((r) => r.data),
};

export const CentralExamsAPI = {
  getCentralExams: () =>
    api.get<CentralExamDto[]>("/central-exams").then((r) => r.data),
  getKpssStudyHome: (variantCode?: string | null) =>
    api.get<CentralExamStudyHomeDto>("/central-exams/kpss", { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  getYksStudyHome: (variantCode?: string | null) =>
    api.get<CentralExamStudyHomeDto>("/central-exams/yks", { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  getLgsStudyHome: (variantCode?: string | null) =>
    api.get<CentralExamStudyHomeDto>("/central-exams/lgs", { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  getYdsStudyHome: (variantCode?: string | null) =>
    api.get<CentralExamStudyHomeDto>("/central-exams/yds", { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  getKpssCountdown: (variantCode?: string | null) =>
    api.get<CentralExamCountdownDto>("/central-exams/kpss/countdown", { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  getKpssTurkceParagrafEntry: (variantCode?: string | null) =>
    api.get<CentralExamPracticeEntryDto>("/central-exams/kpss/turkce-paragraf", { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  startKpssTurkceParagrafPractice: (data: PracticeStartRequestDto = {}) =>
    api.post<PracticeSessionDto>("/central-exams/kpss/turkce-paragraf/start", data).then((r) => r.data),
  submitKpssTurkceParagrafPractice: (data: PracticeSubmitRequestDto) =>
    api.post<PracticeResultDto>("/central-exams/kpss/turkce-paragraf/submit", data).then((r) => r.data),
  getPracticeAttempt: (id: string) =>
    api.get<PracticeResultDto>(`/central-exams/practice-attempts/${id}`).then((r) => r.data),
  getKpssDenemeler: (variantCode?: string | null) =>
    api.get<CentralExamDenemeBlueprintDto[]>("/central-exams/kpss/denemeler", { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  getKpssDeneme: (blueprintCode: string, variantCode?: string | null) =>
    api.get<CentralExamDenemeBlueprintDto>(`/central-exams/kpss/denemeler/${blueprintCode}`, { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  startKpssDeneme: (blueprintCode: string, data: CentralExamDenemeStartRequestDto = {}) =>
    api.post<CentralExamDenemeSessionDto>(`/central-exams/kpss/denemeler/${blueprintCode}/start`, data).then((r) => r.data),
  submitKpssDeneme: (data: CentralExamDenemeSubmitRequestDto) =>
    api.post<CentralExamDenemeResultDto>("/central-exams/kpss/denemeler/submit", data).then((r) => r.data),
  getDenemeAttempt: (id: string) =>
    api.get<CentralExamDenemeResultDto>(`/central-exams/deneme-attempts/${id}`).then((r) => r.data),
  getWarRoom: (examCode: string, params: OrkaLearningContractQuery) =>
    api.get<OrkaExamWarRoomDto>(`/central-exams/${encodeURIComponent(examCode)}/war-room`, { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
};

export const QuestionQualityAPI = {
  getQuestionAnalytics: (questionId: string) =>
    api.get<QuestionItemAnalyticsDto>(`/question-quality/questions/${questionId}`).then((r) => r.data),
  recalculateQuestionAnalytics: (questionId: string) =>
    api.post<RecalculateQuestionAnalyticsResultDto>(`/question-quality/questions/${questionId}/recalculate`).then((r) => r.data),
  getQuestionSignals: (questionId: string) =>
    api.get<QuestionQualityReviewSignalDto[]>(`/question-quality/questions/${questionId}/signals`).then((r) => r.data),
  getCentralExamOverview: (examCode: string, variantCode?: string | null) =>
    api.get<CentralExamQualityOverviewDto>(`/question-quality/central-exams/${examCode}`, { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  recalculateCentralExam: (examCode: string, variantCode?: string | null) =>
    api.post<RecalculateExamAnalyticsResultDto>(`/question-quality/central-exams/${examCode}/recalculate`, null, { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
  getCentralExamCoverage: (examCode: string, variantCode?: string | null) =>
    api.get<CentralExamBlueprintCoverageDto>(`/question-quality/central-exams/${examCode}/coverage`, { params: variantCode ? { variantCode } : undefined }).then((r) => r.data),
};

export const ProductionReadinessAPI = {
  getV1: () => api.get<ProductionReadiness>("/production-readiness/v1").then((r) => r.data),
  purgeExpiredAudio: () => api.post("/production-readiness/retention/audio/purge").then((r) => r.data),
  trimTutorEvents: () => api.post("/production-readiness/redis/tutor-events/trim").then((r) => r.data),
};

export const CodeAPI = {
  /**
   * Kodu Piston sandbox'ında çalıştırır.
   * POST /api/code/run → { stdout, stderr, success }
   */
  run: (data: { code: string; language?: string; sessionId?: string; topicId?: string }) =>
    api.post<{
      stdout: string;
      stderr: string;
      success: boolean;
      phase?: string;
      compileError?: string | null;
      runtimeError?: string | null;
      exitCode?: number | null;
      durationMs?: number;
      truncated?: boolean;
      safeTutorSummary?: string | null;
      runtime?: string | null;
    }>(
      "/code/run",
      {
        code: data.code,
        language: data.language ?? "csharp",
        sessionId: data.sessionId,
        topicId: data.topicId,
      }
    ).then((r) => r.data),
  getLearningIde: (params: OrkaLearningContractQuery) =>
    api.get<OrkaCodeLearningIdeDto>("/code/learning-ide", { params, suppressErrorToast: true } as OrkaAxiosConfig).then((r) => r.data),
};

export const FlashcardsAPI = {
  list: (topicId?: string) =>
    api.get(`/flashcards${topicId ? `?topicId=${topicId}` : ""}`).then((r) => r.data),
  create: (data: {
    topicId?: string;
    front: string;
    back: string;
    hint?: string;
    conceptTag?: string;
    skillTag?: string;
  }) => api.post("/flashcards", data).then((r) => r.data),
  review: (id: string, quality: number, notes?: string) =>
    api.post(`/flashcards/${id}/review`, { quality, notes }).then((r) => r.data),
  delete: (id: string) => api.delete(`/flashcards/${id}`).then((r) => r.data),
};

export const ReviewAPI = {
  due: (topicId?: string) =>
    api.get(`/review/due${topicId ? `?topicId=${topicId}` : ""}`).then((r) => r.data),
  complete: (id: string, quality: number, responseMode = "manual", notes?: string) =>
    api.post(`/review/${id}/complete`, { quality, responseMode, notes }).then((r) => r.data),
};

export const DailyChallengeAPI = {
  today: (topicId?: string) =>
    api.get(`/daily-challenge${topicId ? `?topicId=${topicId}` : ""}`).then((r) => r.data),
  submit: (challengeId: string, answer: string, quality = 3, topicId?: string) =>
    api.post(`/daily-challenge/${challengeId}/submit`, { answer, quality, topicId }).then((r) => r.data),
};

export const BookmarksAPI = {
  list: (topicId?: string) =>
    api.get(`/bookmarks${topicId ? `?topicId=${topicId}` : ""}`).then((r) => r.data),
  create: (data: {
    topicId?: string;
    sessionId?: string;
    messageId?: string;
    learningSourceId?: string;
    wikiPageId?: string;
    reviewItemId?: string;
    flashcardId?: string;
    title?: string;
    note?: string;
    quote?: string;
    tags?: string[];
  }) => api.post("/bookmarks", data).then((r) => r.data),
  update: (id: string, data: { title?: string; note?: string; quote?: string; tags?: string[] }) =>
    api.patch(`/bookmarks/${id}`, data).then((r) => r.data),
  delete: (id: string) => api.delete(`/bookmarks/${id}`).then((r) => r.data),
};

export default api;
