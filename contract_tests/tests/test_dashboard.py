from helpers.config import BASE_URL, ENDPOINTS, TIMEOUT
from helpers.schema_validator import validate_dashboard_stats_schema

def test_get_dashboard_stats_success(session, auth_header):
    url = f"{BASE_URL}{ENDPOINTS['DASHBOARD_STATS']}"
    resp = session.get(url, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200
    data = resp.json()
    validate_dashboard_stats_schema(data)
