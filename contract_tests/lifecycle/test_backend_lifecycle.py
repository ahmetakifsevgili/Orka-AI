import pytest
import requests
import uuid
import time
import os
from helpers.config import BASE_URL

# Lifecycle and AI Smoke Scenario
# Governance: ORKA_RUN_LIFECYCLE=1 environment guard required.

@pytest.mark.lifecycle
@pytest.mark.ai
@pytest.mark.skipif(os.environ.get("ORKA_RUN_LIFECYCLE") != "1", reason="Requires ORKA_RUN_LIFECYCLE=1")
def test_backend_lifecycle_scenario():
    session = requests.Session()
    unique_id = uuid.uuid4().hex[:8]
    email = f"lifecycle_{unique_id}@example.com"
    password = "Password123!"

    # 1. Register
    reg_resp = session.post(f"{BASE_URL}/api/auth/register", json={
        "email": email,
        "password": password,
        "firstName": "Lifecycle",
        "lastName": "Test"
    })
    assert reg_resp.status_code == 201, f"Registration failed: {reg_resp.text}"
    token = reg_resp.json().get("token")
    assert token, "No token in registration response"

    # 2. Setup Auth Header
    session.headers.update({"Authorization": f"Bearer {token}"})

    # 3. Create Topic
    topic_title = f"Modern Web Security {unique_id}"
    topic_resp = session.post(f"{BASE_URL}/api/topics", json={"title": topic_title})
    assert topic_resp.status_code == 200, f"Topic creation failed: {topic_resp.text}"
    topic_id = topic_resp.json().get("id")
    assert topic_id, "No topicId in response"

    # 4. Plan Mode
    plan_resp = session.post(f"{BASE_URL}/api/chat/send", json={
        "content": f"Create a learning plan for {topic_title}.",
        "topicId": topic_id,
        "isPlanMode": True
    })
    assert plan_resp.status_code == 200, f"Plan mode failed: {plan_resp.text}"

    # Extract SessionId (Body then Header)
    session_id = plan_resp.json().get("sessionId")
    if not session_id:
        session_id = plan_resp.headers.get("X-Orka-SessionId")

    assert session_id, "No sessionId in plan mode response (body or header)"

    # 5. Wiki Fallback Check
    briefing_resp = session.get(f"{BASE_URL}/api/wiki/{topic_id}/briefing")
    assert briefing_resp.status_code == 200
    # Fallback/Pending is acceptable

    glossary_resp = session.get(f"{BASE_URL}/api/wiki/{topic_id}/glossary")
    assert glossary_resp.status_code == 200
    # Empty list is acceptable for immature topic

    # 6. Chat Context Accumulation (3-4 messages)
    chat_messages = [
        "What is Cross-Site Scripting (XSS)?",
        "How does a CSRF attack work?",
        "Explain the importance of the HttpOnly flag on cookies.",
        "Give me a multiple choice question about XSS."
    ]

    for msg in chat_messages:
        chat_resp = session.post(f"{BASE_URL}/api/chat/send", json={
            "content": msg,
            "topicId": topic_id,
            "sessionId": session_id
        })
        assert chat_resp.status_code == 200, f"Chat message failed: {msg}"

    # 7. Quiz Generation
    quiz_gen_resp = session.get(f"{BASE_URL}/api/quiz/generate", params={"topicId": topic_id})
    assert quiz_gen_resp.status_code == 200, f"Quiz generation failed: {quiz_gen_resp.text}"

    quiz_data = quiz_gen_resp.json()
    # Handle both list and object results if applicable
    questions = quiz_data if isinstance(quiz_data, list) else quiz_data.get("questions", [])

    if not questions:
        pytest.fail("No questions generated for topic")

    question = questions[0]

    # 8. Adaptive Quiz Attempt (Record)
    # Metadata should be present if LLM provided it
    concept_tag = question.get("conceptTag") or "XSS-Security"
    skill_tag = question.get("skillTag") or "Web-Security"

    attempt_resp = session.post(f"{BASE_URL}/api/quiz/attempt", json={
        "topicId": topic_id,
        "sessionId": session_id,
        "questionId": question.get("id"),
        "question": question.get("question", "Verification Question"),
        "selectedOptionId": "opt-invalid", # Intentionally wrong
        "isCorrect": False,
        "explanation": "Test explanation",
        "conceptTag": concept_tag,
        "skillTag": skill_tag,
        "learningObjective": question.get("learningObjective", "Test Objective"),
        "questionType": question.get("questionType", "conceptual")
    })
    assert attempt_resp.status_code == 200, f"Quiz attempt recording failed: {attempt_resp.text}"

    # 9. Dashboard/Profile Verification
    dash_resp = session.get(f"{BASE_URL}/api/dashboard/stats")
    assert dash_resp.status_code == 200, f"Dashboard check failed: {dash_resp.text}"

    profile_resp = session.get(f"{BASE_URL}/api/user/me")
    assert profile_resp.status_code == 200, f"Profile check failed: {profile_resp.text}"

    # 10. End Session & Wait for Consolidation
    end_resp = session.post(f"{BASE_URL}/api/chat/session/end", json={"sessionId": session_id})
    # Optional endpoint check
    if end_resp.status_code in [200, 204]:
        print("Session ended successfully. Waiting for background consolidation...")
        time.sleep(10)
    else:
        print(f"Session end endpoint returned {end_resp.status_code}, might not be fully implemented.")

    # 11. Final Wiki Check
    final_briefing = session.get(f"{BASE_URL}/api/wiki/{topic_id}/briefing")
    assert final_briefing.status_code == 200

    # 12. Korteks Ping
    ping_resp = session.get(f"{BASE_URL}/api/korteks/ping")
    assert ping_resp.status_code == 200

    print("Lifecycle Hard Test scenario completed successfully.")
