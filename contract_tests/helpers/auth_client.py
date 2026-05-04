import requests
import time
import random
import string
from .config import BASE_URL, ENDPOINTS, TIMEOUT

def generate_unique_email(prefix="testuser"):
    timestamp = int(time.time())
    rand_suffix = ''.join(random.choices(string.ascii_lowercase + string.digits, k=4))
    return f"{prefix}_{timestamp}_{rand_suffix}@example.com"

class AuthClient:
    def __init__(self, session=None):
        self.session = session or requests.Session()

    def register(self, email, password, first_name="Test", last_name="User"):
        payload = {
            "email": email,
            "password": password,
            "firstName": first_name,
            "lastName": last_name
        }
        url = f"{BASE_URL}{ENDPOINTS['AUTH_REGISTER']}"
        return self.session.post(url, json=payload, timeout=TIMEOUT)

    def login(self, email, password):
        payload = {
            "email": email,
            "password": password
        }
        url = f"{BASE_URL}{ENDPOINTS['AUTH_LOGIN']}"
        return self.session.post(url, json=payload, timeout=TIMEOUT)

    def get_token(self, email, password):
        resp = self.login(email, password)
        resp.raise_for_status()
        data = resp.json()

        # Strict contract check: 'token' is the canonical key confirmed by AuthResponse.cs
        token = data.get("token")
        if not token:
            raise KeyError(f"Login response missing 'token' field. Actual keys: {list(data.keys())}")
        return token

    def refresh(self, refresh_token):
        payload = {
            "refreshToken": refresh_token
        }
        url = f"{BASE_URL}{ENDPOINTS['AUTH_REFRESH']}"
        return self.session.post(url, json=payload, timeout=TIMEOUT)
