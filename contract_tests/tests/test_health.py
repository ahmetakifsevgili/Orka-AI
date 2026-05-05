import requests
from helpers.config import BASE_URL, ENDPOINTS, TIMEOUT

def test_get_health_live():
    url = f"{BASE_URL}{ENDPOINTS['HEALTH_LIVE']}"
    resp = requests.get(url, timeout=TIMEOUT)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("status") == "alive"

def test_get_health_ready():
    url = f"{BASE_URL}{ENDPOINTS['HEALTH_READY']}"
    resp = requests.get(url, timeout=TIMEOUT)
    assert resp.status_code == 200
    data = resp.json()
    # Readiness check expects status "Healthy" and canConnect true
    assert data.get("status") == "Healthy"
