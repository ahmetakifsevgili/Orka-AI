import axios from "axios";

const api = axios.create({
  baseURL: "http://localhost:5065/api",
  headers: { "Content-Type": "application/json" },
});

api.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem("orka_token");
    if (token) config.headers.Authorization = `Bearer ${token}`;
    return config;
  },
  (error) => Promise.reject(error)
);

let isRefreshing = false;
let failedQueue = [];

const processQueue = (error, token = null) => {
  failedQueue.forEach(prom => {
    if (error) {
      prom.reject(error);
    } else {
      prom.resolve(token);
    }
  });
  failedQueue = [];
};

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;
    // Don't redirect or retry on 401 for auth endpoints (would cause redirect loop)
    const isAuthEndpoint = originalRequest?.url?.includes('/Auth/');

    if (error.response?.status === 401 && !isAuthEndpoint && !originalRequest._retry) {
      if (isRefreshing) {
        return new Promise(function(resolve, reject) {
          failedQueue.push({ resolve, reject });
        }).then(token => {
          originalRequest.headers.Authorization = 'Bearer ' + token;
          return api(originalRequest);
        }).catch(err => {
          return Promise.reject(err);
        });
      }

      originalRequest._retry = true;
      isRefreshing = true;

      const refresh = localStorage.getItem("orka_refresh");
      if (!refresh) {
        isRefreshing = false;
        localStorage.removeItem("orka_token");
        localStorage.removeItem("orka_refresh");
        localStorage.removeItem("orka_user");
        window.location.href = "/login";
        return Promise.reject(error);
      }

      return new Promise(function (resolve, reject) {
        axios.post('http://localhost:5065/api/Auth/refresh', { refreshToken: refresh })
          .then(({data}) => {
            localStorage.setItem("orka_token", data.token);
            if (data.refreshToken) {
              localStorage.setItem("orka_refresh", data.refreshToken);
            }
            api.defaults.headers.common.Authorization = `Bearer ${data.token}`;
            originalRequest.headers.Authorization = `Bearer ${data.token}`;
            processQueue(null, data.token);
            resolve(api(originalRequest));
          })
          .catch((err) => {
            processQueue(err, null);
            localStorage.removeItem("orka_token");
            localStorage.removeItem("orka_refresh");
            localStorage.removeItem("orka_user");
            window.location.href = "/login";
            reject(err);
          })
          .finally(() => {
            isRefreshing = false;
          });
      });
    }

    return Promise.reject(error);
  }
);

export const AuthAPI = {
  register: (data) => api.post("/Auth/register", data),
  login:    (data) => api.post("/Auth/login", data),
  logout:   (data) => api.post("/Auth/logout", data),
  refresh:  (data) => api.post("/Auth/refresh", data),
};

export const UserAPI = {
  getMe: () => api.get("/User/me"),
};

export const TopicsAPI = {
  getTopics:   ()         => api.get("/Topics"),
  createTopic: (data)     => api.post("/Topics", data),
  getTopic:    (id)       => api.get(`/Topics/${id}`),
  updateTopic: (id, data) => api.patch(`/Topics/${id}`, data),
  deleteTopic: (id)       => api.delete(`/Topics/${id}`),
  getLatestSession: (id)  => api.get(`/Topics/${id}/sessions/latest`),
};

export const ChatAPI = {
  sendMessage: (data) => api.post("/Chat/message", data),
  endSession:  (data) => api.post("/Chat/session/end", data),
};

export const WikiAPI = {
  getTopicPages: (topicId) => api.get(`/Wiki/${topicId}`),
  getPage:       (pageId)  => api.get(`/Wiki/page/${pageId}`),
  addNote:       (pageId, data)  => api.post(`/Wiki/page/${pageId}/note`, data),
  updateBlock:   (blockId, data) => api.put(`/Wiki/block/${blockId}`, data),
  deleteBlock:   (blockId)       => api.delete(`/Wiki/block/${blockId}`),
};

export default api;
