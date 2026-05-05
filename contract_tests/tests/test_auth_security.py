import pytest
import requests
from helpers.config import BASE_URL, ENDPOINTS, TIMEOUT
from helpers.auth_client import generate_unique_email

def test_auth_refresh_flow(auth_client, unique_user):
    # Login to get initial tokens
    resp = auth_client.login(unique_user["email"], unique_user["password"])
    assert resp.status_code == 200
    data = resp.json()
    refresh_token = data.get("refreshToken")
    assert refresh_token, "refreshToken missing in login response"

    # 1. Success Refresh
    refresh_resp = auth_client.refresh(refresh_token)
    assert refresh_resp.status_code == 200
    new_data = refresh_resp.json()
    assert new_data.get("token"), "New access token missing"
    new_refresh_token = new_data.get("refreshToken")
    assert new_refresh_token, "New refresh token missing"

    # 2. Refresh Reuse Protection (Expect 401)
    reuse_resp = auth_client.refresh(refresh_token)
    assert reuse_resp.status_code == 401

def test_auth_refresh_invalid(auth_client):
    # 3. Invalid Refresh Token (Expect 401)
    resp = auth_client.refresh("invalid_token_value")
    assert resp.status_code == 401

@pytest.mark.parametrize("endpoint_key, method", [
    ("TOPICS", "GET"),
    ("USER_ME", "GET"),
    ("USER_SETTINGS", "PATCH"),
    ("DASHBOARD_STATS", "GET")
])
def test_security_guards(session, endpoint_key, method):
    url = f"{BASE_URL}{ENDPOINTS[endpoint_key]}"

    # 1. Missing Authorization header
    resp = session.request(method, url, timeout=TIMEOUT)
    assert resp.status_code == 401

    # 2. Malformed Bearer token
    headers = {"Authorization": "Bearer malformed.token.part"}
    resp = session.request(method, url, headers=headers, timeout=TIMEOUT)
    assert resp.status_code == 401

def test_login_security_guards(auth_client, unique_user):
    # 1. Valid email, empty password (Expect 401)
    # Note: Security contract established in lab v5/v6.
    resp = auth_client.login(unique_user["email"], "")
    assert resp.status_code == 401

    # 2. Valid email, wrong password (Expect 401)
    resp = auth_client.login(unique_user["email"], "WrongPassword123!")
    assert resp.status_code == 401

def test_registration_validation(auth_client):
    # 1. Empty email (Expect 400 - Fixed Application Bug)
    email = ""
    password = "StrongPassword123!"
    resp = auth_client.register(email, password)
    assert resp.status_code == 400

    # 2. Invalid email format (Expect 400 - Fixed Application Bug)
    email = "invalid-email-format"
    resp = auth_client.register(email, password)
    assert resp.status_code == 400
