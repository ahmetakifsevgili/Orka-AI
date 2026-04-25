import requests
import uuid
from requests.auth import HTTPBasicAuth

def test_postapiauthregisterwithvaliddata():
    base_url = "http://localhost:5065"
    url = f"{base_url}/api/auth/register"

    auth = HTTPBasicAuth("admin", "admin1234")

    unique_email = f"testuser-{uuid.uuid4()}@example.com"
    payload = {
        "email": unique_email,
        "password": "ValidPassw0rd!"
    }

    headers = {
        "Content-Type": "application/json"
    }

    # Register user
    response = requests.post(url, json=payload, headers=headers, auth=auth, timeout=30)
    try:
        assert response.status_code == 201, f"Expected 201 Created, got {response.status_code}"
        json_response = response.json()
        assert "userId" in json_response, "Response JSON missing 'userId'"
        assert isinstance(json_response.get("userId"), str) and json_response.get("userId"), "'userId' is empty or not a string"
        assert "basicProfile" in json_response, "Response JSON missing 'basicProfile'"

    finally:
        # Clean up: Delete the created user if possible (assuming such endpoint exists)
        # Since the PRD does not specify a delete user endpoint, no deletion implemented.
        pass

test_postapiauthregisterwithvaliddata()