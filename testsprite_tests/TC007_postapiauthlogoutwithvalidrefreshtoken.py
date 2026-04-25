import requests

BASE_URL = "http://localhost:5065"
EMAIL = "admin"
PASSWORD = "admin1234"
TIMEOUT = 30

def test_postapiauthlogoutwithvalidrefreshtoken():
    # Step 1: Login to get a valid refreshToken
    login_url = f"{BASE_URL}/api/auth/login"
    login_payload = {
        "email": EMAIL,
        "password": PASSWORD
    }
    try:
        login_response = requests.post(login_url, json=login_payload, timeout=TIMEOUT)
    except Exception as e:
        assert False, f"Login request failed with exception: {e}"
    assert login_response.status_code == 200, f"Expected 200 OK from login, got {login_response.status_code}"
    try:
        tokens = login_response.json()
    except Exception as e:
        assert False, f"Failed to decode login response JSON: {e}"
    assert "refreshToken" in tokens, "Login response missing 'refreshToken'"
    refresh_token = tokens["refreshToken"]
    
    # Step 2: Logout using the valid refreshToken
    logout_url = f"{BASE_URL}/api/auth/logout"
    logout_payload = {
        "refreshToken": refresh_token
    }
    try:
        logout_response = requests.post(logout_url, json=logout_payload, timeout=TIMEOUT)
    except Exception as e:
        assert False, f"Logout request failed with exception: {e}"
    assert logout_response.status_code == 200, f"Expected 200 OK from logout, got {logout_response.status_code}"
    # Optionally verify response content indicates token revoked
    try:
        logout_json = logout_response.json()
    except Exception:
        logout_json = None
    # If response JSON present, it should indicate token revoked (assuming message or similar)
    if logout_json:
        assert any(
            "revoked" in str(v).lower() for v in logout_json.values()
        ), "Logout response does not confirm token revoked"

test_postapiauthlogoutwithvalidrefreshtoken()