// ── Orka API Client ──────────────────────────────────────────────

const API_BASE = '/api';

function getToken() { return localStorage.getItem('orka_token'); }
function getRefreshToken() { return localStorage.getItem('orka_refresh'); }
function setTokens(token, refresh) {
    localStorage.setItem('orka_token', token);
    localStorage.setItem('orka_refresh', refresh);
}
function clearTokens() {
    localStorage.removeItem('orka_token');
    localStorage.removeItem('orka_refresh');
    localStorage.removeItem('orka_user');
}
function getUser() {
    try { return JSON.parse(localStorage.getItem('orka_user') || 'null'); } catch { return null; }
}
function setUser(user) { localStorage.setItem('orka_user', JSON.stringify(user)); }

async function apiFetch(path, options = {}) {
    const token = getToken();
    const headers = {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(options.headers || {})
    };

    let res = await fetch(`${API_BASE}${path}`, { ...options, headers });

    if (res.status === 401 && getRefreshToken()) {
        const refreshed = await tryRefresh();
        if (refreshed) {
            headers.Authorization = `Bearer ${getToken()}`;
            res = await fetch(`${API_BASE}${path}`, { ...options, headers });
        } else {
            clearTokens();
            window.location.href = '/login.html';
            return;
        }
    }

    if (!res.ok) {
        const err = await res.json().catch(() => ({ message: `HTTP ${res.status}` }));
        throw { status: res.status, message: err.message || 'Bir hata olustu.' };
    }

    const text = await res.text();
    return text ? JSON.parse(text) : null;
}

async function tryRefresh() {
    try {
        const refresh = getRefreshToken();
        const res = await fetch(`${API_BASE}/auth/refresh`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ refreshToken: refresh })
        });
        if (!res.ok) return false;
        const data = await res.json();
        setTokens(data.token, data.refreshToken);
        return true;
    } catch { return false; }
}

const Auth = {
    async register(email, password) {
        const data = await apiFetch('/auth/register', { method: 'POST', body: JSON.stringify({ email, password }) });
        setTokens(data.token, data.refreshToken);
        setUser(data.user);
        return data;
    },
    async login(email, password) {
        const data = await apiFetch('/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) });
        setTokens(data.token, data.refreshToken);
        setUser(data.user);
        return data;
    },
    async logout() {
        try { await apiFetch('/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken: getRefreshToken() }) }); } catch {}
        clearTokens();
        window.location.href = '/login.html';
    },
    isLoggedIn() { return !!getToken(); }
};

const Topics = {
    async list() { return apiFetch('/topics'); },
    async get(id) { return apiFetch(`/topics/${id}`); },
    async update(id, body) { return apiFetch(`/topics/${id}`, { method: 'PATCH', body: JSON.stringify(body) }); },
    async delete(id) { return apiFetch(`/topics/${id}`, { method: 'DELETE' }); },
    async getLatestSession(topicId) { return apiFetch(`/topics/${topicId}/sessions/latest`); }
};

const Chat = {
    async sendMessage(content, topicId = null, sessionId = null) {
        return apiFetch('/chat/message', { method: 'POST', body: JSON.stringify({ content, topicId, sessionId }) });
    },
    async endSession(sessionId) {
        return apiFetch('/chat/session/end', { method: 'POST', body: JSON.stringify({ sessionId }) });
    }
};

const Wiki = {
    async getPages(topicId) { return apiFetch(`/wiki/${topicId}`); },
    async getPage(pageId) { return apiFetch(`/wiki/page/${pageId}`); },
    async addNote(pageId, content) {
        return apiFetch(`/wiki/page/${pageId}/note`, { method: 'POST', body: JSON.stringify({ content }) });
    },
    async updateBlock(blockId, data) { return apiFetch(`/wiki/block/${blockId}`, { method: 'PUT', body: JSON.stringify(data) }); },
    async deleteBlock(blockId) { return apiFetch(`/wiki/block/${blockId}`, { method: 'DELETE' }); }
};

const UserApi = {
    async me() { return apiFetch('/user/me'); }
};

function showToast(message, type = 'info') {
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        container.style.cssText = 'position:fixed;bottom:20px;right:20px;z-index:9999;display:flex;flex-direction:column;gap:8px;pointer-events:none;';
        document.body.appendChild(container);
    }
    const colors = { success: '#7ee8a2', error: '#e85b8d', info: '#5b8dee' };
    const icons = { success: '&#10003;', error: '&#10005;', info: '&#x2139;' };
    const toast = document.createElement('div');
    toast.style.cssText = `background:#18191c;border:1px solid ${colors[type]};border-left:3px solid ${colors[type]};color:#e2e4e8;padding:10px 16px;border-radius:8px;font-size:13px;display:flex;align-items:center;gap:8px;max-width:320px;box-shadow:0 4px 20px rgba(0,0,0,0.4);opacity:1;transition:opacity 0.3s;`;
    toast.innerHTML = `<span style="color:${colors[type]};font-weight:700;">${icons[type]}</span> ${message}`;
    container.appendChild(toast);
    setTimeout(() => { toast.style.opacity = '0'; setTimeout(() => toast.remove(), 300); }, 3000);
}
