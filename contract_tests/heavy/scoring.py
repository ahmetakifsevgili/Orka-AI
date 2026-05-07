from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from typing import Any

CRITICAL_FAIL = "FAIL"
PASS = "PASS"
PASS_WITH_NOTE = "PASS_WITH_NOTE"
BLOCKED = "BLOCKED_WITH_REASON"


def normalize(value: Any) -> str:
    text = "" if value is None else str(value)
    text = text.lower()
    for src, dst in {"ı": "i", "İ": "i", "ğ": "g", "ü": "u", "ş": "s", "ö": "o", "ç": "c"}.items():
        text = text.replace(src, dst)
    synonyms = {
        "matematik": "math",
        "algoritma": "algorithm",
        "algoritmalar": "algorithm",
        "siralama": "sorting",
        "veri yapilari": "data structures",
        "mulakat": "interview",
        "olasilik": "probability",
        "kombinasyon": "combination",
        "kombinatorik": "combination",
        "combinatorics": "combination",
        "permutasyon": "permutation",
        "paragraf": "paragraph",
        "turkce": "turkish",
        "hizlanmak": "speed",
        "hiz": "speed",
        "dikkat": "attention",
        "hata": "mistake",
        "yas": "age",
        "age problem solvings": "age problems",
        "speed problem solvings": "speed problems",
        "problem solvings": "problems",
        "okuma": "reading",
        "strateji": "strategy",
        "stratejisi": "strategy",
        "sorgu": "query",
        "indeksleme": "index",
        "indeks": "index",
        "optimizasyon": "optimization",
        "optmzasyon": "optimization",
        "veritabani": "database",
        "asenkron": "async",
        "paralel": "parallel",
        "akicilik": "fluency",
        "konusma": "speaking",
        "problem cozme": "problem solving",
        "problemler": "problems",
        "formul": "formula",
        "anlam": "comprehension",
        "ana fikir": "main idea",
    }
    for src, dst in synonyms.items():
        text = re.sub(rf"\b{re.escape(src)}\b", dst, text)
    return re.sub(r"\s+", " ", text).strip()


def contains_any(text: str, terms: list[str]) -> bool:
    haystack = normalize(text)
    return any(normalize(term) in haystack for term in terms)


def count_matches(text: str, terms: list[str]) -> int:
    haystack = normalize(text)
    return sum(1 for term in terms if normalize(term) in haystack)


@dataclass
class ScoreResult:
    scenario_id: str
    score: int
    status: str
    evidence: list[str] = field(default_factory=list)
    issues: list[str] = field(default_factory=list)

    def add_issue(self, issue: str) -> None:
        self.issues.append(issue)

    def add_evidence(self, evidence: str) -> None:
        self.evidence.append(evidence)


def score_intent(scenario: dict[str, Any], intent: dict[str, Any]) -> ScoreResult:
    result = ScoreResult(scenario_id=scenario["id"], score=0, status=PASS)
    main = intent.get("mainTopic") or intent.get("MainTopic") or ""
    focus = intent.get("focusArea") or intent.get("FocusArea") or ""
    research = intent.get("researchIntent") or intent.get("ResearchIntent") or ""
    requires_confirmation = intent.get("requiresUserConfirmation")
    if requires_confirmation is None:
        requires_confirmation = intent.get("RequiresUserConfirmation")

    main_score = 20 if contains_any(main, scenario["expected_main"]) else 0
    focus_score = min(20, count_matches(focus, scenario["expected_focus"]) * 10)
    research_score = min(35, count_matches(research, scenario["expected_research"]) * 12)
    confirmation_score = 15 if requires_confirmation is True else 0
    raw_penalty = -20 if normalize(research) == normalize(scenario["request"]) else 0
    forbidden_penalty = -20 if contains_any(research, scenario.get("forbidden_terms", [])) else 0

    result.score = max(0, min(100, main_score + focus_score + research_score + confirmation_score + 10 + raw_penalty + forbidden_penalty))
    result.add_evidence(f"mainTopic={main}")
    result.add_evidence(f"focusArea={focus}")
    result.add_evidence(f"researchIntent={research}")
    result.add_evidence(f"requiresUserConfirmation={requires_confirmation}")

    if main_score == 0:
        result.add_issue("main topic does not match expected domain")
    if focus_score == 0:
        result.add_issue("focus area does not expose the requested learning focus")
    if research_score < 24:
        result.add_issue("research intent is too thin for Korteks")
    if requires_confirmation is not True:
        result.add_issue("intent preview does not require user confirmation")
    if raw_penalty:
        result.add_issue("research intent equals raw user message")
        result.status = CRITICAL_FAIL
    if forbidden_penalty:
        result.add_issue("research intent contains forbidden cross-domain leakage")
        result.status = CRITICAL_FAIL
    if result.status != CRITICAL_FAIL:
        result.status = PASS if result.score >= 85 else PASS_WITH_NOTE
    return result


def parse_questions(questions_json: str | list[Any] | dict[str, Any]) -> list[dict[str, Any]]:
    if isinstance(questions_json, list):
        return [q for q in questions_json if isinstance(q, dict)]
    if isinstance(questions_json, dict):
        nested = questions_json.get("questions") or questions_json.get("Questions") or []
        return [q for q in nested if isinstance(q, dict)]
    if not questions_json:
        return []
    try:
        return parse_questions(json.loads(questions_json))
    except json.JSONDecodeError:
        return []


def question_text(question: dict[str, Any]) -> str:
    parts: list[str] = []
    for key in ("question", "Question", "text", "Text", "prompt", "Prompt", "code", "Code"):
        if question.get(key):
            parts.append(str(question[key]))
    options = question.get("options") or question.get("Options") or question.get("choices") or question.get("Choices")
    if isinstance(options, dict):
        parts.extend(str(v) for v in options.values())
    elif isinstance(options, list):
        for option in options:
            if isinstance(option, dict):
                parts.extend(str(v) for v in option.values() if isinstance(v, str))
            else:
                parts.append(str(option))
    return "\n".join(parts)


def score_quiz(scenario: dict[str, Any], questions: list[dict[str, Any]]) -> ScoreResult:
    result = ScoreResult(scenario_id=scenario["id"], score=0, status=PASS)
    count = len(questions)
    text = "\n".join(question_text(q) for q in questions)
    normalized = normalize(text)
    count_score = 25 if 15 <= count <= 25 else 0
    domain_score = min(30, count_matches(text, scenario["expected_quiz_terms"]) * 8)
    structure_score = 20 if count > 0 and all(has_options(q) for q in questions[: min(count, 5)]) else 5
    forbidden_penalty = -25 if contains_any(text, scenario.get("forbidden_terms", [])) else 0
    duplicate_penalty = -20 if has_duplicate_questions(questions) else 0
    leakage_penalty = -30 if has_answer_leakage(questions) else 0
    internal_penalty = -20 if any(token in normalized for token in ["plan:practice", "plan:deepdive", "unknown skill", "orka ide", "sandbox"]) else 0

    result.score = max(0, min(100, count_score + domain_score + structure_score + 25 + forbidden_penalty + duplicate_penalty + leakage_penalty + internal_penalty))
    result.add_evidence(f"questionCount={count}")
    result.add_evidence(f"domainTermMatches={count_matches(text, scenario['expected_quiz_terms'])}")

    if count_score == 0:
        result.add_issue("quiz question count is outside 15-25")
        result.status = CRITICAL_FAIL
    if forbidden_penalty:
        result.add_issue("quiz contains forbidden cross-domain leakage")
        result.status = CRITICAL_FAIL
    if leakage_penalty:
        result.add_issue("quiz option leaks correct/wrong answer labels")
        result.status = CRITICAL_FAIL
    if duplicate_penalty:
        result.add_issue("quiz contains repeated questions")
    if internal_penalty:
        result.add_issue("quiz leaks internal system/product labels")
        result.status = CRITICAL_FAIL
    if result.status != CRITICAL_FAIL:
        result.status = PASS if result.score >= 85 else PASS_WITH_NOTE
    return result


def has_options(question: dict[str, Any]) -> bool:
    options = question.get("options") or question.get("Options") or question.get("choices") or question.get("Choices")
    return isinstance(options, (list, dict)) and len(options) >= 3


def has_duplicate_questions(questions: list[dict[str, Any]]) -> bool:
    seen: set[str] = set()
    for question in questions:
        text = normalize(question.get("question") or question.get("Question") or question.get("text") or "")
        text = re.sub(r"\d+", "", text)
        if not text:
            continue
        if text in seen:
            return True
        seen.add(text)
    return False


def has_answer_leakage(questions: list[dict[str, Any]]) -> bool:
    leakage = re.compile(r"\b(dogru|yanlis|correct|wrong)\s+(yaklasim|secenek|answer|option)\b", re.IGNORECASE)
    for question in questions:
        options = question.get("options") or question.get("Options") or question.get("choices") or question.get("Choices") or []
        values: list[str] = []
        if isinstance(options, dict):
            values = [str(v) for v in options.values()]
        elif isinstance(options, list):
            for option in options:
                if isinstance(option, dict):
                    values.extend(str(v) for v in option.values() if isinstance(v, str))
                else:
                    values.append(str(option))
        if any(leakage.search(normalize(value)) for value in values):
            return True
    return False


def score_tutor_response(scenario: dict[str, Any], content: str, metadata: dict[str, Any] | None = None) -> ScoreResult:
    result = ScoreResult(scenario_id=scenario["id"], score=0, status=PASS)
    metadata = metadata or {}
    content_norm = normalize(content)
    relevance = min(25, count_matches(content, scenario["expected_quiz_terms"] + scenario["expected_focus"]) * 5)
    next_step = 15 if contains_any(content, ["sonraki adim", "pratik", "deneyelim", "orka ide", "exercise", "practice"]) else 0
    ide_priority = 15
    if scenario["domain"] in ("java-algorithms", "java-data-structures", "csharp-async", "python-pandas", "sql-optimization"):
        ide_priority = 15 if contains_any(content, ["orka ide", "sandbox", "kod", "calistir", "run"]) else 0
    external_ide_penalty = -25 if "visual studio" in content_norm and "orka ide" not in content_norm else 0
    fake_claim_penalty = -30 if contains_any(content, ["kaynaklarina gore", "wikine gore", "kaydedildi", "zayif alanin"]) and not metadata else 0
    safety = 20 if not contains_any(content, scenario.get("forbidden_terms", [])) else 0
    structure = 15 if len(content.strip()) >= 120 else 5
    result.score = max(0, min(100, relevance + next_step + ide_priority + safety + structure + 10 + external_ide_penalty + fake_claim_penalty))
    result.add_evidence(f"contentLength={len(content)}")
    result.add_evidence(f"metadataPresent={bool(metadata)}")
    if external_ide_penalty:
        result.add_issue("Tutor prioritizes Visual Studio without first framing Orka IDE/sandbox")
        result.status = CRITICAL_FAIL
    if fake_claim_penalty:
        result.add_issue("Tutor makes persisted/source-backed learning claim without metadata")
        result.status = CRITICAL_FAIL
    if relevance < 10:
        result.add_issue("Tutor response is weakly connected to scenario domain")
    if next_step == 0:
        result.add_issue("Tutor response does not give a clear next step")
    if ide_priority == 0:
        result.add_issue("coding Tutor response does not foreground Orka IDE/sandbox practice")
    if result.status != CRITICAL_FAIL:
        result.status = PASS if result.score >= 85 else PASS_WITH_NOTE
    return result


def aggregate(results: list[ScoreResult]) -> dict[str, Any]:
    if not results:
        return {"score": 0, "status": BLOCKED, "count": 0}
    score = round(sum(r.score for r in results) / len(results), 2)
    critical = [r for r in results if r.status == CRITICAL_FAIL]
    blocked = [r for r in results if r.status == BLOCKED]
    if critical:
        status = CRITICAL_FAIL
    elif blocked:
        status = PASS_WITH_NOTE
    elif score >= 85:
        status = PASS
    elif score >= 75:
        status = PASS_WITH_NOTE
    else:
        status = CRITICAL_FAIL
    return {"score": score, "status": status, "count": len(results), "criticalFailCount": len(critical), "blockedCount": len(blocked)}
