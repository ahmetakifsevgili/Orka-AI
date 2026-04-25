import requests

BASE_URL = "http://localhost:5065"
TIMEOUT = 30

def test_postapiauthloginwithinvalidcredentials():
    url = f"{BASE_URL}/api/auth/login"
    payload = {
        "email": "invalid_user@example.com",
        "password": "wrong_password"
    }
    headers = {
        "Content-Type": "application/json"
    }
    response = requests.post(url, json=payload, headers=headers, timeout=TIMEOUT)
    assert response.status_code == 401, f"Expected 401 Unauthorized, got {response.status_code}"
    try:
        data = response.json()
    except Exception:
        data = {}
    error_message = data.get("error") or data.get("message") or ""
    assert "invalid credentials" in error_message.lower(), f"Expected 'invalid credentials' in error message, got {error_message}"

test_postapiauthloginwithinvalidcredentials()