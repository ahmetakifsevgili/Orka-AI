import os

# Base URL from environment or the canonical local dev API port.
BASE_URL = os.getenv("ORKA_API_URL", "http://localhost:5065").rstrip("/")

# Global request timeout
TIMEOUT = 30

# Endpoints
ENDPOINTS = {
    "HEALTH_LIVE": "/health/live",
    "HEALTH_READY": "/health/ready",
    "AUTH_REGISTER": "/api/auth/register",
    "AUTH_LOGIN": "/api/auth/login",
    "AUTH_REFRESH": "/api/auth/refresh",
    "TOPICS": "/api/topics",
    "USER_ME": "/api/user/me",
    "USER_SETTINGS": "/api/user/settings",
    "DASHBOARD_STATS": "/api/dashboard/stats"
}
