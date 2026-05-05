import pytest
import requests
import uuid
from helpers.config import BASE_URL, ENDPOINTS, TIMEOUT

def test_topic_persistence(session, auth_header):
    url = f"{BASE_URL}{ENDPOINTS['TOPICS']}"
    unique_title = f"Persistence Test {uuid.uuid4()}"

    # 1. Create Topic
    payload = {"title": unique_title}
    create_resp = session.post(url, json=payload, headers=auth_header, timeout=TIMEOUT)
    assert create_resp.status_code == 200

    # 2. Verify Persistence in List
    list_resp = session.get(url, headers=auth_header, timeout=TIMEOUT)
    assert list_resp.status_code == 200
    topics = list_resp.json()
    assert any(t.get("title") == unique_title for t in topics), "Created topic not found in list"

def test_user_data_isolation(auth_client, session):
    # Register and login User A
    user_a = {"email": f"usera_{uuid.uuid4()}@example.com", "password": "Password123!"}
    auth_client.register(user_a["email"], user_a["password"])
    token_a = auth_client.get_token(user_a["email"], user_a["password"])

    # Register and login User B
    user_b = {"email": f"userb_{uuid.uuid4()}@example.com", "password": "Password123!"}
    auth_client.register(user_b["email"], user_b["password"])
    token_b = auth_client.get_token(user_b["email"], user_b["password"])

    url = f"{BASE_URL}{ENDPOINTS['TOPICS']}"
    topic_title = f"Private Topic {uuid.uuid4()}"

    # User A creates a topic
    session.post(url, json={"title": topic_title}, headers={"Authorization": f"Bearer {token_a}"}, timeout=TIMEOUT)

    # User B checks list
    list_resp = session.get(url, headers={"Authorization": f"Bearer {token_b}"}, timeout=TIMEOUT)
    topics_b = list_resp.json()
    assert not any(t.get("title") == topic_title for t in topics_b), "User B saw User A's private topic"

def test_settings_persistence(session, auth_header):
    # Verified Logic: PATCH -> GET /api/user/me
    patch_url = f"{BASE_URL}{ENDPOINTS['USER_SETTINGS']}"
    me_url = f"{BASE_URL}{ENDPOINTS['USER_ME']}"

    # 1. Update settings
    # We use 'Dark' as verified in lab v5/v6
    payload = {"theme": "Dark", "language": "Turkish", "soundsEnabled": False}
    patch_resp = session.patch(patch_url, json=payload, headers=auth_header, timeout=TIMEOUT)
    assert patch_resp.status_code in (200, 204)

    # 2. Verify via /api/user/me
    me_resp = session.get(me_url, headers=auth_header, timeout=TIMEOUT)
    assert me_resp.status_code == 200
    data = me_resp.json()
    settings = data.get("settings", {})

    assert settings.get("theme") == "Dark"
    assert settings.get("language") == "Turkish"
    assert settings.get("soundsEnabled") is False

def test_dashboard_schema(session, auth_header):
    url = f"{BASE_URL}{ENDPOINTS['DASHBOARD_STATS']}"
    resp = session.get(url, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200
    data = resp.json()

    # Canonical contract key confirmed in lab Phase 3
    assert "totalTopics" in data, f"Key 'totalTopics' missing. Actual keys: {list(data.keys())}"

def test_topic_empty_title(session, auth_header):
    """
    CONTRACT DECISION / PLACEHOLDER BEHAVIOR:
    POST /api/topics with empty title returns 200 OK.
    This is explicitly documented as intentional system behavior.
    """
    url = f"{BASE_URL}{ENDPOINTS['TOPICS']}"
    payload = {"title": ""}
    resp = session.post(url, json=payload, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200

def test_malformed_request_guards(session, auth_header):
    # 1. Malformed JSON with valid JWT
    url = f"{BASE_URL}{ENDPOINTS['TOPICS']}"
    malformed_json = '{"title": "Missing Quote}'
    resp = session.post(url, data=malformed_json, headers={**auth_header, "Content-Type": "application/json"}, timeout=TIMEOUT)
    assert resp.status_code == 400

    # 2. Invalid Data Type (Expected vs Actual)
    url = f"{BASE_URL}{ENDPOINTS['USER_SETTINGS']}"
    resp = session.patch(url, json=["Not", "A", "Dictionary"], headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 400
