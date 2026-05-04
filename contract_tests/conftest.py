import pytest
import requests
from helpers.auth_client import AuthClient, generate_unique_email

@pytest.fixture(scope="session")
def session():
    return requests.Session()

@pytest.fixture
def auth_client(session):
    return AuthClient(session)

@pytest.fixture
def unique_user(auth_client):
    email = generate_unique_email()
    password = "StrongPassword123!"
    resp = auth_client.register(email, password)
    assert resp.status_code == 201, f"Initial registration failed: {resp.text}"
    return {"email": email, "password": password}

@pytest.fixture
def auth_token(auth_client, unique_user):
    return auth_client.get_token(unique_user["email"], unique_user["password"])

@pytest.fixture
def auth_header(auth_token):
    return {"Authorization": f"Bearer {auth_token}"}
