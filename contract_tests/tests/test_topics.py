import time
import requests
from helpers.config import BASE_URL, ENDPOINTS, TIMEOUT
from helpers.schema_validator import validate_topic_schema

def test_get_topics_returns_list(session, auth_header):
    url = f"{BASE_URL}{ENDPOINTS['TOPICS']}"
    resp = session.get(url, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200
    data = resp.json()
    assert isinstance(data, list)

def test_post_topic_success(session, auth_header):
    url = f"{BASE_URL}{ENDPOINTS['TOPICS']}"
    title = f"Clean QA Topic {int(time.time())}"
    payload = {"title": title}

    resp = session.post(url, json=payload, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200
    data = resp.json()

    validate_topic_schema(data)
    assert data["title"] == title
