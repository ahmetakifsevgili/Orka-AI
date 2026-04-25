import requests

def test_postapiauthlogoutwithoutrefreshtoken():
    base_url = "http://localhost:5065"
    url = f"{base_url}/api/auth/logout"
    headers = {
        "Content-Type": "application/json"
    }
    # No payload with refreshToken to test unauthorized access
    try:
        response = requests.post(url, headers=headers, timeout=30)  # Removed json={} here
        assert response.status_code == 401, f"Expected 401 Unauthorized, got {response.status_code}"
    except requests.RequestException as e:
        assert False, f"Request failed: {e}"

test_postapiauthlogoutwithoutrefreshtoken()