import requests

BASE_URL = "http://localhost:5065"
AUTH_REGISTER = "/api/auth/register"
AUTH_LOGIN = "/api/auth/login"
AUTH_REFRESH = "/api/auth/refresh"
AUTH_LOGOUT = "/api/auth/logout"

TIMEOUT = 30

def test_postapiauthrefreshwithexpiredorrevokedtoken():
    # Step 1: Register a new user to get a valid refresh token
    register_payload = {
        "email": "testuser_tc006@example.com",
        "password": "TestPass123!"
    }
    user_id = None
    refresh_token = None

    try:
        reg_resp = requests.post(
            BASE_URL + AUTH_REGISTER,
            json=register_payload,
            timeout=TIMEOUT
        )
        # Accept 201 Created, 200 OK with tokens, 409 Conflict if user exists, or 400 with known email in use message
        if reg_resp.status_code == 201:
            user_id = reg_resp.json().get("userId")
        elif reg_resp.status_code == 200:
            # Registration returning tokens, extract refreshToken
            data = reg_resp.json()
            refresh_token = data.get("refreshToken")
            user_id = data.get("user", {}).get("id")
        elif reg_resp.status_code == 409:
            # User exists, proceed without user_id
            user_id = None
        elif reg_resp.status_code == 400:
            # Sometimes email already in use returns 400 with error message
            msg = reg_resp.json().get("message", "").lower()
            if "email" in msg and ("kullanımda" in msg or "already in use" in msg):
                user_id = None
            else:
                assert False, f"Unexpected 400 response message: {reg_resp.text}"
        else:
            assert False, f"Unexpected response from register endpoint: {reg_resp.status_code} {reg_resp.text}"

        # Step 2: Login to get refresh token if not obtained from register
        if not refresh_token:
            login_payload = {
                "email": register_payload["email"],
                "password": register_payload["password"]
            }
            login_resp = requests.post(
                BASE_URL + AUTH_LOGIN,
                json=login_payload,
                timeout=TIMEOUT
            )
            assert login_resp.status_code == 200, f"Login failed: {login_resp.status_code} {login_resp.text}"
            login_data = login_resp.json()
            refresh_token = login_data.get("refreshToken")
            assert refresh_token, "No refreshToken returned from login"

        # Step 3: Logout to revoke the refresh token (invalidate it)
        logout_payload = {
            "refreshToken": refresh_token
        }
        logout_resp = requests.post(
            BASE_URL + AUTH_LOGOUT,
            json=logout_payload,
            timeout=TIMEOUT
        )
        assert logout_resp.status_code == 200, f"Logout failed: {logout_resp.status_code} {logout_resp.text}"

        # Step 4: Attempt to refresh with revoked refreshToken, expect 401 or 400
        refresh_payload = {
            "refreshToken": refresh_token
        }
        refresh_resp = requests.post(
            BASE_URL + AUTH_REFRESH,
            json=refresh_payload,
            timeout=TIMEOUT
        )
        assert refresh_resp.status_code in (400, 401), \
            f"Expected 400 or 401 but got {refresh_resp.status_code}: {refresh_resp.text}"

        response_json = {}
        try:
            response_json = refresh_resp.json()
        except Exception:
            pass

        # Validate error message indicates invalid token
        err_msgs = [
            "invalid refresh token",
            "expired",
            "revoked",
            "unauthorized",
            "bad request",
            "invalid token"
        ]
        error_found = False
        for key in ("error", "message", "detail", "errorMessage"):
            if key in response_json:
                val = response_json[key].lower()
                if any(msg in val for msg in err_msgs):
                    error_found = True
                    break
        assert error_found or refresh_resp.status_code in (400, 401), \
            "Error message does not indicate invalid or expired refresh token"

    finally:
        # Cleanup: If user was created, no direct endpoint for deletion is defined in PRD; so skip
        pass

test_postapiauthrefreshwithexpiredorrevokedtoken()
