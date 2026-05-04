import uuid

from helpers.config import BASE_URL, TIMEOUT


def _create_topic(session, auth_header, title=None, plan_intent=None):
    payload = {"title": title or f"Contract Topic {uuid.uuid4()}"}
    if plan_intent:
        payload["category"] = f"Plan:{plan_intent}"
        payload["planIntent"] = plan_intent
    resp = session.post(f"{BASE_URL}/api/topics", json=payload, headers=auth_header, timeout=TIMEOUT)
    assert resp.status_code == 200, resp.text
    return resp.json()


def test_topic_plan_intent_shape(session, auth_header):
    topic = _create_topic(session, auth_header, plan_intent="QuickReview")

    assert topic.get("planIntent") == "QuickReview"

    list_resp = session.get(f"{BASE_URL}/api/topics", headers=auth_header, timeout=TIMEOUT)
    assert list_resp.status_code == 200
    listed = next(t for t in list_resp.json() if t["id"] == topic["id"])
    assert listed.get("planIntent") == "QuickReview"


def test_review_due_empty_state(session, auth_header):
    resp = session.get(f"{BASE_URL}/api/review/due", headers=auth_header, timeout=TIMEOUT)

    assert resp.status_code == 200
    assert isinstance(resp.json(), list)


def test_wrong_quiz_creates_review_and_review_complete(session, auth_header):
    topic = _create_topic(session, auth_header)
    payload = {
        "topicId": topic["id"],
        "questionId": f"q-{uuid.uuid4()}",
        "question": "Which concept is being checked?",
        "selectedOptionId": "wrong",
        "isCorrect": False,
        "explanation": "The answer missed the target concept.",
        "skillTag": "ContractSkill",
        "conceptTag": "ContractConcept",
        "learningObjective": "Complete durable review",
        "questionType": "concept",
        "mistakeCategory": "Conceptual",
        "topicPath": "Contracts > Review",
        "difficulty": "easy",
        "questionHash": f"hash-{uuid.uuid4()}",
    }

    attempt = session.post(f"{BASE_URL}/api/quiz/attempt", json=payload, headers=auth_header, timeout=TIMEOUT)
    assert attempt.status_code == 200, attempt.text
    assert attempt.json()["review"]

    due = session.get(f"{BASE_URL}/api/review/due?topicId={topic['id']}", headers=auth_header, timeout=TIMEOUT)
    assert due.status_code == 200, due.text
    items = due.json()
    assert items

    complete = session.post(
        f"{BASE_URL}/api/review/{items[0]['id']}/complete",
        json={"quality": 4, "notes": "contract test"},
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert complete.status_code == 200, complete.text
    body = complete.json()
    assert body["id"] == items[0]["id"]
    assert body["lastReviewedAt"]


def test_flashcard_create_links_review_and_review_submit(session, auth_header):
    topic = _create_topic(session, auth_header)
    payload = {
        "topicId": topic["id"],
        "front": "What causes an async deadlock?",
        "back": "Blocking on async work can capture and wait on the same context.",
        "conceptTag": "AsyncDeadlock",
        "skillTag": "CSharpAsync",
        "learningObjective": "Avoid blocking async code",
        "createdFrom": "contract-test",
    }

    create_resp = session.post(f"{BASE_URL}/api/flashcards", json=payload, headers=auth_header, timeout=TIMEOUT)
    assert create_resp.status_code == 200, create_resp.text
    card = create_resp.json()
    assert card["reviewItemId"]

    review_resp = session.post(
        f"{BASE_URL}/api/flashcards/{card['id']}/review",
        json={"quality": 5},
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert review_resp.status_code == 200, review_resp.text
    reviewed = review_resp.json()
    assert reviewed["reviewItemId"] == card["reviewItemId"]


def test_daily_challenge_durable_submit_is_idempotent(session, auth_header):
    topic = _create_topic(session, auth_header)

    get_resp = session.get(f"{BASE_URL}/api/daily-challenge?topicId={topic['id']}", headers=auth_header, timeout=TIMEOUT)
    assert get_resp.status_code == 200, get_resp.text
    challenge = get_resp.json()
    assert challenge["id"]

    payload = {"answer": "short answer", "quality": 4}
    first = session.post(
        f"{BASE_URL}/api/daily-challenge/{challenge['id']}/submit",
        json=payload,
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert first.status_code == 200, first.text
    first_body = first.json()
    assert first_body["duplicate"] is False
    assert first_body["xpAwarded"] > 0

    second = session.post(
        f"{BASE_URL}/api/daily-challenge/{challenge['id']}/submit",
        json=payload,
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert second.status_code == 200, second.text
    second_body = second.json()
    assert second_body["duplicate"] is True
    assert second_body["xpAwarded"] == first_body["xpAwarded"]


def test_notifications_empty_list_shape(session, auth_header):
    resp = session.get(f"{BASE_URL}/api/notifications", headers=auth_header, timeout=TIMEOUT)

    assert resp.status_code == 200
    assert isinstance(resp.json(), list)

    read_all = session.post(f"{BASE_URL}/api/notifications/read-all", headers=auth_header, timeout=TIMEOUT)
    assert read_all.status_code == 200, read_all.text


def test_audio_unknown_job_returns_404(session, auth_header):
    resp = session.get(f"{BASE_URL}/api/audio/overview/{uuid.uuid4()}", headers=auth_header, timeout=TIMEOUT)

    assert resp.status_code == 404


def test_source_delete_hides_source_chunks(session, auth_header):
    topic = _create_topic(session, auth_header)
    files = {
        "file": (
            "contract-source.txt",
            b"Contract unique source fact: blue prism async marker.",
            "text/plain",
        )
    }
    data = {"topicId": topic["id"]}

    upload = session.post(
        f"{BASE_URL}/api/sources/upload",
        files=files,
        data=data,
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert upload.status_code == 200, upload.text
    source = upload.json()
    assert source["id"]

    delete = session.delete(f"{BASE_URL}/api/sources/{source['id']}", headers=auth_header, timeout=TIMEOUT)
    assert delete.status_code == 200, delete.text
    assert delete.json()["deleted"] is True

    ask_deleted = session.post(
        f"{BASE_URL}/api/sources/{source['id']}/ask",
        json={"question": "What is the contract unique source fact?"},
        headers=auth_header,
        timeout=TIMEOUT,
    )
    assert ask_deleted.status_code == 404, ask_deleted.text
