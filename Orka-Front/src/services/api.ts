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
    const original = error.config as InternalAxiosRequestConfig & {
      _retry?: boolean;
    };
    const isAuthRoute = original?.url?.startsWith("/Auth/");

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
        const { data } = await axios.post<AuthTokens>("/api/Auth/refresh", {
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
    if (!isAuthRoute) {
      const status = error.response?.status;
      const url = original?.url ?? "bilinmeyen endpoint";
      const endpointLabel = url.split("/").slice(-2).join("/");

      if (!error.response) {
        // Network error / sunucuya ulaşılamıyor
        toast.error(`Sunucuya bağlanılamıyor (${endpointLabel})`, { id: `net-${endpointLabel}` });
      } else if (status === 404) {
        toast.error(`Hata: ${endpointLabel} bulunamadı (404)`, { id: `404-${endpointLabel}` });
      } else if (status && status >= 500) {
        toast.error(`Hata: ${endpointLabel} sunucu hatası (${status})`, { id: `5xx-${endpointLabel}` });
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
  getLatestSession: (id: string) => api.get(`/topics/${id}/sessions/latest`),
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
};

/**
 * QuizAPI — Quiz denemelerini backend'e kaydeder.
 * POST /Quiz/attempt endpoint'i backend'de hazır olmadığında
 * sessizce başarısız olur (fire-and-forget).
 */export const QuizAPI = {
  recordAttempt: (data: {
    messageId: string;
    question: string;
    selectedOptionId: string;
    isCorrect: boolean;
    explanation: string;
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
  run: (data: { code: string; language?: string }) =>
    api.post<{ stdout: string; stderr: string; success: boolean }>(
      "/code/run",
      { code: data.code, language: data.language ?? "csharp" }
    ).then((r) => r.data),
};

export default api;
