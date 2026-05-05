import pytest
from helpers.auth_client import generate_unique_email

def test_auth_registration_success(auth_client):
    email = generate_unique_email("reg_success")
    password = "ValidPassword123!"
    resp = auth_client.register(email, password)
    assert resp.status_code == 201
    data = resp.json()
    assert "id" in data or "userId" in data

def test_auth_duplicate_email_returns_409(auth_client):
    email = generate_unique_email("duplicate")
    password = "ValidPassword123!"

    # First attempt
    auth_client.register(email, password)

    # Second attempt
    resp = auth_client.register(email, password)
    assert resp.status_code == 409

def test_auth_login_success(auth_client, unique_user):
    resp = auth_client.login(unique_user["email"], unique_user["password"])
    assert resp.status_code == 200
    data = resp.json()
    assert "token" in data
    assert "refreshToken" in data

def test_auth_login_wrong_password_returns_401(auth_client, unique_user):
    resp = auth_client.login(unique_user["email"], "WrongPassword!")
    assert resp.status_code == 401
