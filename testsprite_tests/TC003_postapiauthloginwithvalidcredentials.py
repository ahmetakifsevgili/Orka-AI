import requests


def test_postapiauthloginwithvalidcredentials():
    base_url = "http://localhost:5065"
    login_url = f"{base_url}/api/auth/login"
    payload = {"email": "admin@example.com", "password": "admin1234"}

    try:
        response = requests.post(
            login_url,
            json=payload,
            timeout=30
        )
    except requests.RequestException as e:
        assert False, f"Request failed: {e}"

    assert response.status_code == 200, f"Expected status code 200, got {response.status_code}"

    try:
        json_resp = response.json()
    except ValueError:
        assert False, "Response is not valid JSON"

    assert "accessToken" in json_resp, "Response JSON missing 'accessToken'"
    assert "refreshToken" in json_resp, "Response JSON missing 'refreshToken'"
    assert "expiresIn" in json_resp, "Response JSON missing 'expiresIn'"
    assert isinstance(json_resp["accessToken"], str) and json_resp["accessToken"], "'accessToken' is empty or not a string"
    assert isinstance(json_resp["refreshToken"], str) and json_resp["refreshToken"], "'refreshToken' is empty or not a string"
    assert isinstance(json_resp["expiresIn"], int) and json_resp["expiresIn"] > 0, "'expiresIn' is not a positive integer"


test_postapiauthloginwithvalidcredentials()
