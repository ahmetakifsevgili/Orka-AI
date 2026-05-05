import uuid

from helpers.config import BASE_URL, TIMEOUT


def _create_topic(session, auth_header):
    payload = {"title": f"Parity Topic {uuid.uuid4()}"}
    resp = session.post(f"{BASE_URL}/api/topics", json=payload, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200, resp.text
    return resp.json()


def test_bookmarks_crud_and_empty_state(session, auth_header):
    empty = session.get(f"{BASE_URL}/api/bookmarks", headers=auth_header, timeout=TIMEOUT)
    assert empty.status_code == 200, empty.text
    assert isinstance(empty.json(), list)

    topic = _create_topic(session, auth_header)
    create = session.post(
        f"{BASE_URL}/api/bookmarks",
        json={
            "topicId": topic["id"],
            "title": "Important parity bookmark",
            "note": "Keep this for review",
            "tags": ["parity", "backend"],
        },
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert create.status_code == 200, create.text
    bookmark = create.json()
    assert bookmark["topicId"] == topic["id"]
    assert bookmark["status"] == "active"
    assert "parity" in bookmark["tags"]

    listed = session.get(f"{BASE_URL}/api/bookmarks?topicId={topic['id']}", headers=auth_header, timeout=TIMEOUT)
    assert listed.status_code == 200, listed.text
    assert any(item["id"] == bookmark["id"] for item in listed.json())

    update = session.patch(
        f"{BASE_URL}/api/bookmarks/{bookmark['id']}",
        json={"note": "Updated note"},
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert update.status_code == 200, update.text
    assert update.json()["note"] == "Updated note"

    delete = session.delete(f"{BASE_URL}/api/bookmarks/{bookmark['id']}", headers=auth_header, timeout=TIMEOUT)
    assert delete.status_code == 200, delete.text
    assert delete.json()["deleted"] is True


def test_feature_parity_aliases(session, auth_header):
    health = session.get(f"{BASE_URL}/api/health/live", timeout=TIMEOUT)
    assert health.status_code == 200, health.text

    xp = session.get(f"{BASE_URL}/api/profile/xp", headers=auth_header, timeout=TIMEOUT)
    assert xp.status_code == 200, xp.text
    assert "totalXP" in xp.json()

    badges = session.get(f"{BASE_URL}/api/profile/badges", headers=auth_header, timeout=TIMEOUT)
    assert badges.status_code == 200, badges.text
    assert "badges" in badges.json()

    # Alias should validate like /api/code/run without requiring a provider call.
    code = session.post(
        f"{BASE_URL}/api/code/execute",
        json={"language": "python", "code": ""},
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert code.status_code == 400, code.text


def test_push_subscription_crud(session, auth_header):
    endpoint = f"https://push.example.test/{uuid.uuid4()}"
    create = session.post(
        f"{BASE_URL}/api/notifications/subscriptions",
        json={
            "endpoint": endpoint,
            "p256dh": "test-key",
            "auth": "test-auth",
            "deviceLabel": "contract browser",
        },
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert create.status_code == 200, create.text
    item = create.json()
    assert item["endpoint"] == endpoint
    assert item["status"] == "active"

    listed = session.get(f"{BASE_URL}/api/push/subscriptions", headers=auth_header, timeout=TIMEOUT)
    assert listed.status_code == 200, listed.text
    assert any(row["id"] == item["id"] for row in listed.json())

    deleted = session.delete(
        f"{BASE_URL}/api/notifications/subscriptions/{item['id']}",
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert deleted.status_code == 200, deleted.text
    assert deleted.json()["deleted"] is True
