import requests

BASE_URL = "http://localhost:5065"
TIMEOUT = 30

def test_postapiauthregisterwithexistingemail():
    register_url = f"{BASE_URL}/api/auth/register"
    test_email = "existinguser@example.com"
    test_password = "TestPass123!"

    # First, ensure that the email is registered
    payload = {"email": test_email, "password": test_password}

    # Register the user (ignore if it already exists)
    try:
        resp_initial = requests.post(register_url, json=payload, timeout=TIMEOUT)
        # If 201 Created or 409 Conflict, proceed
        assert resp_initial.status_code in (201, 409)
    except requests.RequestException as e:
        assert False, f"Setup registration request failed: {e}"

    # Attempt to register with the same email again to trigger 409 Conflict
    try:
        resp = requests.post(register_url, json=payload, timeout=TIMEOUT)
    except requests.RequestException as e:
        assert False, f"Request failed: {e}"

    assert resp.status_code == 409, f"Expected status code 409, got {resp.status_code}"
    json_resp = {}
    try:
        json_resp = resp.json()
    except ValueError:
        assert False, "Response is not valid JSON"

    error_message = json_resp.get('error') or json_resp.get('message') or ''
    assert "email already in use" in error_message.lower(), f"Expected error message 'email already in use', got: {error_message}"

test_postapiauthregisterwithexistingemail()
