import axios, {
  type AxiosInstance,
  type InternalAxiosRequestConfig,
  type AxiosResponse,
} from "axios";
import toast from "react-hot-toast";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface AuthTokens {
  token: string;
  refreshToken: string;
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
const REFRESH_KEY = "orka_refresh";
const USER_KEY = "orka_user";

export const storage = {
  getToken: () => localStorage.getItem(TOKEN_KEY),
  getRefresh: () => localStorage.getItem(REFRESH_KEY),
  getUser: (): AuthUser | null => {
    const raw = localStorage.getItem(USER_KEY);
    return raw ? (JSON.parse(raw) as AuthUser) : null;
  },
  save: (data: AuthResponse) => {
    localStorage.setItem(TOKEN_KEY, data.token);
    localStorage.setItem(REFRESH_KEY, data.refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(data.user));
  },
  clear: () => {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
    // Home.tsx context kalıcılığı — logout'ta temizlensin ki farklı
    // kullanıcı girişinde önceki kullanıcının ekranı açılmasın.
    localStorage.removeItem("orka_active_topic_id");
    localStorage.removeItem("orka_active_view");
    localStorage.removeItem("orka_wiki_topic_id");
  },
};

// ---------------------------------------------------------------------------
// Axios instance
// Requests go to /api/... — Vite proxy forwards them to localhost:5065/api/...
// ---------------------------------------------------------------------------

const api: AxiosInstance = axios.create({
  baseURL: "/api",
  headers: { "Content-Type": "application/json" },
});

type OrkaAxiosConfig = InternalAxiosRequestConfig & {
  suppressErrorToast?: boolean;
};

const isAuthUrl = (url?: string) => {
  const normalized = (url ?? "").toLowerCase();
  return normalized.startsWith("/auth/") || normalized.startsWith("/api/auth/");
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

api.interceptors.response.use(
  (res: AxiosResponse) => res,
  async (error) => {
    const original = error.config as OrkaAxiosConfig & {
      _retry?: boolean;
    };
    const isAuthRoute = isAuthUrl(original?.url);

    if (error.response?.status === 401 && !isAuthRoute && !original._retry) {
      if (isRefreshing) {
        return new Promise<string>((resolve, reject) => {
          pendingQueue.push({ resolve, reject });
        }).then((token) => {
          original.headers.Authorization = `Bearer ${token}`;
          return api(original);
        });
      }

      original._retry = true;
      isRefreshing = true;

      const refresh = storage.getRefresh();
      if (!refresh) {
        storage.clear();
        isRefreshing = false;
        if (!window.location.pathname.startsWith("/login")) {
          window.location.href = "/login";
        }
        return Promise.reject(error);
      }

      try {
        const { data } = await axios.post<AuthTokens>("/api/auth/refresh", {
          refreshToken: refresh,
        });
        localStorage.setItem(TOKEN_KEY, data.token);
        if (data.refreshToken) localStorage.setItem(REFRESH_KEY, data.refreshToken);
        api.defaults.headers.common.Authorization = `Bearer ${data.token}`;
        flushQueue(null, data.token);
        original.headers.Authorization = `Bearer ${data.token}`;
        return api(original);
      } catch (refreshError) {
        flushQueue(refreshError);
        storage.clear();
        if (!window.location.pathname.startsWith("/login")) {
          window.location.href = "/login";
        }
        return Promise.reject(refreshError);
      } finally {
        isRefreshing = false;
      }
    }

    // Global hata bildirimi (401 ve auth rotaları hariç)
    if (!isAuthRoute && !original?.suppressErrorToast) {
      const status = error.response?.status;
      const url = original?.url ?? "bilinmeyen endpoint";
      const endpointLabel = url.split("/").slice(-2).join("/");
      const correlationId = error.response?.headers?.["x-correlation-id"];
      const suffix = correlationId ? ` · id: ${correlationId}` : "";

      if (!error.response) {
        // Network error / sunucuya ulaşılamıyor
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
  logout: (refreshToken: string) =>
    api.post("/auth/logout", { refreshToken }),
  refresh: (refreshToken: string) =>
    api.post<AuthTokens>("/auth/refresh", { refreshToken }),
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
    focusTopicId?: string;
    focusTopicPath?: string;
    focusSourceRef?: string;
    content: string;
    isPlanMode?: boolean;
  }) => api.post("/chat/message", data),
  
  streamMessage: async (data: {
    topicId?: string;
    sessionId?: string;
    focusTopicId?: string;
    focusTopicPath?: string;
    focusSourceRef?: string;
    content: string;
    isPlanMode?: boolean;
  }) => {
    const token = storage.getToken();
    return fetch("/api/chat/stream", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Authorization": `Bearer ${token}`
      },
      body: JSON.stringify(data)
    });
  },

  endSession: (data: { sessionId: string }) =>
    api.post("/chat/session/end", data),
};

export const DashboardAPI = {
  getStats: () => api.get("/dashboard/stats"),
  getRecentActivity: () => api.get("/dashboard/recent-activity"),
  getSystemHealth: () => api.get("/dashboard/system-health"),
  getDevDiagnostics: () => api.get("/dev/diagnostics/config"),
};

export const WikiAPI = {
  getTopicPages: (topicId: string) => api.get(`/wiki/${topicId}`),
  getPage: (pageId: string) => api.get(`/wiki/page/${pageId}`),
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
  ask: (sourceId: string, question: string) =>
    api.post<{
      answer: string;
      citations: Array<{
        id: string;
        pageNumber: number;
        chunkIndex: number;
        text: string;
        highlightHint?: string;
      }>;
    }>(`/sources/${sourceId}/ask`, { question }).then((r) => r.data),
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
  streamUrl: (jobId: string) => `/api/audio/overview/${jobId}/stream`,
};

export interface BookmarkItem {
  id: string;
  messageId: string;
  topicId?: string | null;
  topicTitle?: string | null;
  note?: string | null;
  tag?: string | null;
  messageRole: string;
  messageSnippet: string;
  messageCreatedAt: string;
  createdAt: string;
}

export const BookmarksAPI = {
  list: () => api.get<BookmarkItem[]>("/bookmarks").then((r) => r.data),
  create: (data: { messageId: string; note?: string; tag?: string }) =>
    api.post<{ id: string; alreadyExisted: boolean }>("/bookmarks", data).then((r) => r.data),
  update: (id: string, data: { note?: string; tag?: string }) =>
    api.patch<{ id: string }>(`/bookmarks/${id}`, data).then((r) => r.data),
  remove: (id: string) => api.delete(`/bookmarks/${id}`),
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
};

/**
 * QuizAPI — Quiz denemelerini backend'e kaydeder.
 * POST /api/quiz/attempt endpoint'i backend'de hazır olmadığında
 * sessizce başarısız olur (fire-and-forget).
 */export const QuizAPI = {
  recordAttempt: (data: {
    messageId: string;
    quizRunId?: string;
    questionId?: string;
    topicId?: string;
    sessionId?: string;
    question: string;
    selectedOptionId: string;
    isCorrect: boolean;
    explanation: string;
    skillTag?: string;
    topicPath?: string;
    difficulty?: string;
    cognitiveType?: string;
    questionHash?: string;
    sourceRefsJson?: string;
  }) => api.post("/quiz/attempt", data),
  getGlobalStats: () => api.get("/quiz/stats"),
  getHistory: (topicId: string) => api.get(`/quiz/history/${topicId}`),
};

export const KorteksAPI = {
  // Düz konu araştırması (opsiyonel URL)
  stream: (data: { topic: string; topicId?: string; sourceUrl?: string }) => {
    const token = storage.getToken();
    return fetch("/api/korteks/research", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(data),
    });
  },

  // Dosya yükleyerek araştırma (PDF / TXT / MD)
  streamWithFile: (data: { topic: string; topicId?: string; file: File }) => {
    const token = storage.getToken();
    const form = new FormData();
    form.append("topic", data.topic);
    if (data.topicId) form.append("topicId", data.topicId);
    form.append("file", data.file);
    return fetch("/api/korteks/research-file", {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      body: form,
    });
  },
};

export const CodeAPI = {
  /**
   * Kodu Piston sandbox'ında çalıştırır.
   * POST /api/code/run → { stdout, stderr, success }
   */
  run: (data: { code: string; language?: string; sessionId?: string; topicId?: string }) =>
    api.post<{ stdout: string; stderr: string; success: boolean }>(
      "/code/run",
      {
        code: data.code,
        language: data.language ?? "csharp",
        sessionId: data.sessionId,
        topicId: data.topicId,
      }
    ).then((r) => r.data),
};

export default api;
