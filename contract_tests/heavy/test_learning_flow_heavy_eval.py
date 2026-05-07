from __future__ import annotations

import json
import os
import pathlib
import time
import uuid
from typing import Any

import pytest
import requests

from helpers.config import BASE_URL, TIMEOUT
from heavy.scoring import BLOCKED, CRITICAL_FAIL, ScoreResult, aggregate, parse_questions, score_intent, score_quiz, score_tutor_response

RUN_HEAVY = os.environ.get("ORKA_RUN_HEAVY_EVAL") == "1"
FULL_FLOW_LIMIT = int(os.environ.get("ORKA_HEAVY_FULL_FLOW_LIMIT", "3"))
SCENARIO_PATH = pathlib.Path(__file__).with_name("scenarios.json")

pytestmark = [
    pytest.mark.heavy,
    pytest.mark.ai,
    pytest.mark.skipif(not RUN_HEAVY, reason="Set ORKA_RUN_HEAVY_EVAL=1 to run heavy learning-flow evals"),
]


def load_scenarios() -> list[dict[str, Any]]:
    return json.loads(SCENARIO_PATH.read_text(encoding="utf-8"))


def register_session(prefix: str) -> requests.Session:
    session = requests.Session()
    unique = f"{int(time.time())}_{uuid.uuid4().hex[:8]}"
    response = session.post(
        f"{BASE_URL}/api/auth/register",
        json={"email": f"{prefix}_{unique}@example.com", "password": "StrongPassword123!", "firstName": "Heavy", "lastName": "Eval"},
        timeout=TIMEOUT,
    )
    assert response.status_code == 201, f"registration failed: {response.status_code} {response.text}"
    token = response.json().get("token")
    assert token, "registration response did not include token"
    session.headers.update({"Authorization": f"Bearer {token}"})
    return session


def create_topic(session: requests.Session, title: str, plan_intent: str | None = None) -> str:
    response = session.post(
        f"{BASE_URL}/api/topics",
        json={"title": title, "emoji": "📘", "category": "Heavy Eval", "planIntent": plan_intent},
        timeout=TIMEOUT,
    )
    assert response.status_code == 200, f"topic creation failed: {response.status_code} {response.text}"
    topic_id = response.json().get("id")
    assert topic_id, "topic creation response missing id"
    return topic_id


def analyze_intent(session: requests.Session, scenario: dict[str, Any], topic_id: str | None = None) -> dict[str, Any]:
    payload: dict[str, Any] = {"rawRequest": scenario["request"]}
    if topic_id:
        payload["topicId"] = topic_id
    response = session.post(f"{BASE_URL}/api/quiz/plan-diagnostic/intent", json=payload, timeout=TIMEOUT)
    assert response.status_code == 200, f"intent analysis failed for {scenario['id']}: {response.status_code} {response.text}"
    return response.json()


def read_field(payload: dict[str, Any], camel_key: str) -> Any:
    pascal_key = camel_key[:1].upper() + camel_key[1:]
    return payload.get(camel_key) if camel_key in payload else payload.get(pascal_key)


def assert_start_without_approval_is_rejected(session: requests.Session, topic_id: str) -> None:
    response = session.post(
        f"{BASE_URL}/api/quiz/plan-diagnostic/start",
        json={"topicId": topic_id, "rawStudyRequest": "raw request without approved intent"},
        timeout=TIMEOUT,
    )
    assert response.status_code in (400, 422), (
        "Plan diagnostic accepted a request without approved study intent; "
        f"status={response.status_code} body={response.text}"
    )


def start_diagnostic(session: requests.Session, scenario: dict[str, Any], topic_id: str, intent: dict[str, Any]) -> dict[str, Any]:
    response = session.post(
        f"{BASE_URL}/api/quiz/plan-diagnostic/start",
        json={
            "topicId": topic_id,
            "topicTitle": f"{read_field(intent, 'mainTopic')}: {read_field(intent, 'focusArea')}",
            "rawStudyRequest": scenario["request"],
            "intentRequestId": read_field(intent, "intentRequestId"),
            "approvedMainTopic": read_field(intent, "mainTopic"),
            "approvedFocusArea": read_field(intent, "focusArea"),
            "approvedStudyGoal": read_field(intent, "studyGoal"),
            "approvedResearchIntent": read_field(intent, "researchIntent"),
        },
        timeout=max(TIMEOUT, 90),
    )
    assert response.status_code == 200, f"diagnostic start failed for {scenario['id']}: {response.status_code} {response.text}"
    return response.json()


def record_all_answers(session: requests.Session, scenario: dict[str, Any], start: dict[str, Any], questions: list[dict[str, Any]]) -> None:
    for index, question in enumerate(questions, start=1):
        correct = index % 3 != 0
        response = session.post(
            f"{BASE_URL}/api/quiz/plan-diagnostic/{start['planRequestId']}/attempt",
            json={
                "quizRunId": start.get("quizRunId"),
                "topicId": start.get("topicId"),
                "messageId": f"heavy-{scenario['id']}-{index}",
                "questionId": str(question.get("id") or question.get("questionId") or f"q{index}"),
                "question": str(question.get("question") or question.get("text") or f"{scenario['id']} question {index}"),
                "selectedOptionId": "A",
                "isCorrect": correct,
                "explanation": "Heavy eval simulated answer",
                "skillTag": scenario["domain"],
                "conceptTag": f"{scenario['domain']}-concept-{index}",
                "learningObjective": scenario["request"],
                "questionType": str(question.get("questionType") or "diagnostic"),
                "mistakeCategory": None if correct else "Conceptual",
                "topicPath": scenario["domain"],
                "difficulty": str(question.get("difficulty") or "diagnostic"),
                "cognitiveType": str(question.get("cognitiveType") or "analysis"),
            },
            timeout=TIMEOUT,
        )
        assert response.status_code == 200, f"diagnostic answer failed for {scenario['id']} q{index}: {response.status_code} {response.text}"


def finalize_diagnostic(session: requests.Session, plan_request_id: str) -> dict[str, Any]:
    response = session.post(
        f"{BASE_URL}/api/quiz/plan-diagnostic/finalize",
        json={"planRequestId": plan_request_id},
        timeout=max(TIMEOUT, 90),
    )
    assert response.status_code == 200, f"diagnostic finalize failed: {response.status_code} {response.text}"
    return response.json()


def send_tutor_message(session: requests.Session, scenario: dict[str, Any], topic_id: str) -> dict[str, Any]:
    response = session.post(
        f"{BASE_URL}/api/chat/send",
        json={"topicId": topic_id, "content": scenario["tutor_prompt"], "isPlanMode": False},
        timeout=max(TIMEOUT, 90),
    )
    assert response.status_code == 200, f"Tutor message failed for {scenario['id']}: {response.status_code} {response.text}"
    return response.json()


@pytest.mark.parametrize("scenario", load_scenarios(), ids=lambda s: s["id"])
def test_heavy_intent_gate_scores_all_40_plus_scenarios(scenario: dict[str, Any]) -> None:
    session = register_session("intent_eval")
    topic_id = create_topic(session, f"Intent Eval {scenario['id']}", scenario["request"])
    assert_start_without_approval_is_rejected(session, topic_id)
    intent = analyze_intent(session, scenario, topic_id)
    result = score_intent(scenario, intent)
    assert result.status != CRITICAL_FAIL, json.dumps(result.__dict__, ensure_ascii=False, indent=2)
    assert result.score >= 75, json.dumps(result.__dict__, ensure_ascii=False, indent=2)


def test_heavy_representative_full_flow_scores_plan_quiz_and_tutor() -> None:
    scenarios = load_scenarios()[:FULL_FLOW_LIMIT]
    results: list[ScoreResult] = []
    for scenario in scenarios:
        session = register_session("full_eval")
        topic_id = create_topic(session, f"Full Eval {scenario['id']}", scenario["request"])
        assert_start_without_approval_is_rejected(session, topic_id)
        intent = analyze_intent(session, scenario, topic_id)
        intent_result = score_intent(scenario, intent)
        results.append(intent_result)
        if intent_result.status == CRITICAL_FAIL:
            continue
        try:
            start = start_diagnostic(session, scenario, topic_id, intent)
            questions = parse_questions(start.get("questionsJson") or [])
            quiz_result = score_quiz(scenario, questions)
            results.append(quiz_result)
            if quiz_result.status == CRITICAL_FAIL:
                continue
            record_all_answers(session, scenario, start, questions)
            final = finalize_diagnostic(session, start["planRequestId"])
            assert final.get("planGenerated") is True, f"plan was not generated for {scenario['id']}: {final}"
            tutor = send_tutor_message(session, scenario, topic_id)
            results.append(score_tutor_response(scenario, tutor.get("content", ""), tutor.get("metadata")))
        except requests.RequestException as exc:
            blocked = ScoreResult(scenario_id=scenario["id"], score=0, status=BLOCKED)
            blocked.add_issue(f"runtime request blocked: {exc}")
            results.append(blocked)
    summary = aggregate(results)
    assert summary["status"] != CRITICAL_FAIL, json.dumps({"summary": summary, "results": [r.__dict__ for r in results]}, ensure_ascii=False, indent=2)
