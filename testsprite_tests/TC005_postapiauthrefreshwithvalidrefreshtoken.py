import requests

BASE_URL = "http://localhost:5065"
TIMEOUT = 30
EMAIL = "admin@example.com"  # Use valid email registered in the system
PASSWORD = "admin1234"

def test_postapiauthrefreshwithvalidrefreshtoken():
    login_url = f"{BASE_URL}/api/auth/login"
    refresh_url = f"{BASE_URL}/api/auth/refresh"

    # Login to get a valid refreshToken
    login_payload = {
        "email": EMAIL,
        "password": PASSWORD
    }

    try:
        login_resp = requests.post(login_url, json=login_payload, timeout=TIMEOUT)
        assert login_resp.status_code == 200, f"Login failed, status code: {login_resp.status_code}"
        login_data = login_resp.json()
        assert "refreshToken" in login_data and "accessToken" in login_data, "Missing tokens in login response"
        refresh_token = login_data["refreshToken"]

        # Use refreshToken to get new tokens
        refresh_payload = {"refreshToken": refresh_token}
        refresh_resp = requests.post(refresh_url, json=refresh_payload, timeout=TIMEOUT)
        assert refresh_resp.status_code == 200, f"Refresh token request failed, status code: {refresh_resp.status_code}"
        refresh_data = refresh_resp.json()
        assert "accessToken" in refresh_data and "refreshToken" in refresh_data, "Missing tokens in refresh response"
        # New tokens should be different from old ones
        assert refresh_data["accessToken"] != login_data["accessToken"], "New accessToken should differ from old one"
        assert refresh_data["refreshToken"] != refresh_token, "New refreshToken should differ from old one"
    except requests.RequestException as e:
        assert False, f"HTTP request failed: {e}"


test_postapiauthrefreshwithvalidrefreshtoken()