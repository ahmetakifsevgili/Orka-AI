from helpers.config import BASE_URL, ENDPOINTS, TIMEOUT
from helpers.schema_validator import validate_user_me_schema

def test_get_user_me_success(session, auth_header):
    url = f"{BASE_URL}{ENDPOINTS['USER_ME']}"
    resp = session.get(url, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200
    data = resp.json()
    validate_user_me_schema(data)

def test_patch_user_settings_persistence(session, auth_header):
    settings_url = f"{BASE_URL}{ENDPOINTS['USER_SETTINGS']}"
    me_url = f"{BASE_URL}{ENDPOINTS['USER_ME']}"

    # 1. Update settings
    payload = {"theme": "dark", "soundsEnabled": False}
    patch_resp = session.patch(settings_url, json=payload, headers=auth_header, timeout=TIMEOUT)
    assert patch_resp.status_code == 200

    # 2. Verify persistence
    get_resp = session.get(me_url, headers=auth_header, timeout=TIMEOUT)
    assert get_resp.status_code == 200
    data = get_resp.json()

    assert data["settings"]["theme"] == "dark"
    assert data["settings"]["soundsEnabled"] is False
