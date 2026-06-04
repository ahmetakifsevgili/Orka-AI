import { SCORE_MAX, REQUIRED_ENDPOINTS } from "../scenarios.mjs";
import { forbiddenFieldHits, hasPublicLeak } from "../privacy.mjs";

const STATUS_OK = new Set([
  "ready",
  "ready_for_learning",
  "strong",
  "usable",
  "healthy",
  "completed",
  "ok",
  "pass",
  "available",
  "sufficient",
  "needs_repair",
  "needs_prerequisite_check",
  "source_limited",
  "unknown",
]);

export function evaluateBundle(bundle, { includeJudge = false, judgeResult = null } = {}) {
  const result = {
    personaId: bundle.persona.id,
    scores: Object.fromEntries(Object.keys(SCORE_MAX).map((key) => [key, 0])),
    maxScores: SCORE_MAX,
    issues: [],
    invariants: [],
    judge: judgeResult,
  };

  contractInvariants(bundle, result);
  diagnosticInvariants(bundle, result);
  planInvariants(bundle, result);
  tutorInvariants(bundle, result);
  remediationInvariants(bundle, result);
  groundingPrivacyInvariants(bundle, result);
  coherenceInvariants(bundle, result);
  applyJudge(includeJudge, judgeResult, result);

  result.totalScore = Object.values(result.scores).reduce((sum, value) => sum + value, 0);
  result.criticalFailureCount = result.issues.filter((issue) => issue.severity === "critical").length;
  result.releasePass =
    result.totalScore >= 85 &&
    result.criticalFailureCount === 0 &&
    Object.entries(SCORE_MAX).every(([area, max]) => result.scores[area] >= max * 0.75);
  return result;
}

function contractInvariants(bundle, result) {
  const failedRequired = REQUIRED_ENDPOINTS.filter((key) => {
    const endpoint = bundle.contract.endpoints[key];
    return !endpoint || endpoint.ok !== true || endpoint.parseable !== true;
  });
  passIf(result, "contract", "required_endpoints_ok", failedRequired.length === 0, 7, "required endpoints failed", failedRequired);

  const parseableCount = Object.values(bundle.contract.endpoints).filter((endpoint) => endpoint.parseable).length;
  passIf(result, "contract", "responses_parseable", parseableCount >= Math.max(1, Object.keys(bundle.contract.endpoints).length - 2), 3, "too many non-parseable responses");

  passIf(result, "contract", "identity_flow", Boolean(bundle.ids.topicRef && bundle.ids.planRequestRef && bundle.ids.quizRunRef), 3, "missing required id refs");
  passIf(result, "contract", "evidence_refs_present", Object.values(bundle.contract.endpoints).every((endpoint) => endpoint.ref), 2, "endpoint evidence refs missing");
}

function diagnosticInvariants(bundle, result) {
  const d = bundle.diagnostic;
  passIf(result, "diagnostic", "diagnostic_ids_present", d.planRequestPresent && d.quizRunPresent, 2, "missing planRequestId or quizRunId", [], true);
  passIf(result, "diagnostic", "question_count_range", d.questionCount >= 15 && d.questionCount <= 25, 3, `question count ${d.questionCount}`);
  passIf(result, "diagnostic", "stable_question_ids", d.idsStable, 2, "question ids missing or duplicate");
  passIf(result, "diagnostic", "complete_items", d.stemOptionComplete >= Math.ceil(d.questionCount * 0.9), 3, `${d.stemOptionComplete}/${d.questionCount} complete`);
  passIf(result, "diagnostic", "concept_metadata", d.conceptTagCount === d.questionCount && d.conceptDiversity >= Math.min(5, d.questionCount), 3, "insufficient concept metadata");
  passIf(result, "diagnostic", "diagnostic_skill_mix", d.questionTypeDiversity >= 2 && d.cognitiveSkillDiversity >= 2, 1, "flat diagnostic skill mix");
  passIf(result, "diagnostic", "misconception_probes_present", d.misconceptionProbeCount >= Math.ceil(d.questionCount * 0.4), 1, "insufficient misconception probes");
  passIf(result, "diagnostic", "difficulty_diversity", d.difficultyDiversity >= 2, 1, "flat difficulty");
  passIf(result, "diagnostic", "attempts_recorded", d.answeredCount === d.questionCount, 1, `${d.answeredCount}/${d.questionCount} attempts recorded`);
}

function planInvariants(bundle, result) {
  const p = bundle.plan;
  passIf(result, "plan", "plan_generated", p.generated, 3, "finalize did not generate plan", [], true);
  passIf(result, "plan", "materialized_hierarchy", p.isMaterialized && p.chapterCount >= 6 && p.lessonCount >= 24, 5, `${p.chapterCount} chapters/${p.lessonCount} lessons`, [], true);
  passIf(result, "plan", "no_blocking_issues", p.blockingIssueCount === 0, 4, `${p.blockingIssueCount} blocking issues`, [], p.blockingIssueCount > 0);
  passIf(result, "plan", "quality_scores_professional", allAtLeast(0.75, p.specificityScore, p.sequencingScore, p.assessmentAlignmentScore, p.tutorAlignmentScore) && Number(p.evidenceAlignmentScore) >= 0.60, 2, "numeric plan quality scores below professional floor");
  passIf(result, "plan", "readiness_not_failed", isPositiveStatus(p.readinessStatus) || p.readinessStatus === "unknown", 2, `readiness=${p.readinessStatus}`);
  passIf(result, "plan", "plan_contract_available", p.planStepCount >= 24 || p.repairLoopCount > 0 || p.checkpointCoverage !== undefined, 1, "plan contract/repair/checkpoint evidence unavailable");
  passIf(result, "plan", "plan_step_contract_complete", planStepCoverageOk(p), 3, "plan steps missing objective/concept/hooks/success criteria", [], p.planStepCount >= 24 && !planStepCoverageOk(p));
  passIf(result, "plan", "source_and_evidence_status_structured", Boolean(p.sourceReadiness || p.learnerEvidenceStatus), 2, "source/evidence readiness unavailable");
}

function tutorInvariants(bundle, result) {
  const t = bundle.tutor;
  const pg = bundle.pedagogy;
  const tooling = bundle.tooling;
  passIf(result, "tutor_pedagogy", "tutor_answer_and_trace", t.answerPresent && t.traceIdPresent, 3, "missing tutor answer or action trace", [], true);
  passIf(result, "tutor_pedagogy", "professional_contract_present", t.professionalContractPresent && t.rawPayloadExposed === false, 3, "professional tutor contract unavailable or unsafe", [], true);
  passIf(result, "tutor_pedagogy", "teaching_mode_structured", t.teachingMode !== "unknown", 2, "teaching mode unavailable");
  passIf(result, "tutor_pedagogy", "active_concept_structured", Boolean(t.activeConceptKey), 2, "active concept unavailable");
  passIf(result, "tutor_pedagogy", "policy_structured", t.directAnswerPolicy !== "unknown" && t.groundingPolicy !== "unknown", 2, "direct answer or grounding policy unavailable");
  passIf(result, "tutor_pedagogy", "plan_step_bound", Boolean(t.usedPlanStepId), 2, "tutor did not bind turn to a plan step", [], bundle.plan.planStepCount > 0);
  passIf(result, "tutor_pedagogy", "diagnostic_or_weak_signal_used", t.usedDiagnosticSignal !== "unknown" || t.weakConceptKeyCount > 0 || t.usedRepairSignal, 3, "tutor did not expose diagnostic/weak concept signal usage");
  passIf(result, "tutor_pedagogy", "lesson_delivery_structured", !["unknown", "none"].includes(t.lessonDeliveryMode) && t.learnerLevel !== "unknown", 3, "lesson delivery mode/learner level unavailable");
  passIf(result, "tutor_pedagogy", "micro_check_or_next_prompt", t.microCheckObserved || t.nextCheckPromptPresent || pg.rubricScoreCount > 0, 2, "next check/rubric evidence unavailable");
  passIf(result, "tutor_pedagogy", "pedagogy_no_critical", pg.available && pg.hasCriticalViolation === false, 4, "pedagogy critical violation or unavailable", [], pg.hasCriticalViolation === true);
  passIf(result, "tutor_pedagogy", "tooling_governed", tooling.traceCount > 0 && tooling.highRiskCount === 0, 1, "tooling trace missing or high risk");
  passIf(result, "tutor_pedagogy", "policy_context_used", t.contextUseCount > 0 || t.policyQualityStatus !== "unknown" || t.toolDecisionReasonCount > 0 || t.learnerSignalCount > 0, 1, "policy context/quality unavailable");
}

function remediationInvariants(bundle, result) {
  const persona = bundle.persona;
  const mastery = bundle.mastery;
  const tutor = bundle.tutor;
  const mission = bundle.mission;
  const plan = bundle.plan;
  const hasRepair = plan.repairLoopCount > 0 ||
    mastery.repairCount > 0 ||
    ["guided_repair", "prerequisite_review", "repair", "remediation"].includes(tutor.remediationPolicy) ||
    mission.primaryActionType !== "unknown";
  const heavyActive = ["heavy", "urgent", "urgent_remediation", "major_repair"].includes(String(mission.primaryActionType).toLowerCase()) ||
    String(tutor.remediationPolicy).toLowerCase() === "heavy";

  if (persona.expectRemediation) {
    passIf(result, "remediation", "expected_repair_structured", hasRepair || tutor.usedRepairSignal || tutor.remediationRepairType !== "none", 5, "expected remediation did not expose structured repair evidence");
    passIf(result, "remediation", "repair_not_label_only", plan.repairLoopCount > 0 || tutor.nextCheckPromptPresent || tutor.microCheckObserved || mission.coachActionCount > 0, 3, "repair lacks loop/check/action evidence");
  } else {
    passIf(result, "remediation", "no_unexplained_heavy_repair", !heavyActive || mission.reasonCodeCount > 0, 5, "non-struggling learner has unexplained heavy repair");
    passIf(result, "remediation", "learning_action_available", mission.missionAvailable || mission.coachAvailable || tutor.nextCheckPromptPresent, 3, "no next learning action evidence");
  }

  if (persona.blankLearner) {
    passIf(result, "remediation", "blank_not_mastered", mastery.weakConceptCount > 0 || plan.learnerEvidenceStatus !== "mastered", 2, "blank learner appears mastered", [], true);
  } else {
    award(result, "remediation", "blank_contract_not_applicable", 2);
  }
}

function groundingPrivacyInvariants(bundle, result) {
  const refs = bundle.contract.endpoints;
  const rawPublic = JSON.stringify(bundle);
  const forbidden = forbiddenFieldHits(bundle);
  passIf(result, "grounding_privacy", "structural_redaction_clean", forbidden.length === 0, 3, "forbidden fields present after redaction", forbidden, true);
  passIf(result, "grounding_privacy", "secret_pattern_backstop_clean", !hasPublicLeak(rawPublic), 3, "secret/path/raw payload pattern found", [], true);
  if (bundle.persona.sourceSensitive) {
    passIf(result, "grounding_privacy", "source_sensitive_grounding_explicit", (bundle.sourceGrounding.effectiveEvidenceCount ?? bundle.sourceGrounding.sourceCount) > 0 || ["limited", "degraded", "insufficient", "unknown"].includes(String(bundle.sourceGrounding.evidenceStatus).toLowerCase()), 2, "source-sensitive evidence status unavailable");
  } else {
    award(result, "grounding_privacy", "source_sensitive_not_applicable", 2);
  }
  passIf(result, "grounding_privacy", "endpoint_refs_redacted", Object.values(refs).every((endpoint) => endpoint.ref), 2, "missing redacted refs");
}

function coherenceInvariants(bundle, result) {
  const ids = bundle.ids;
  const conceptAligned = Boolean(bundle.tutor.activeConceptKey) &&
    (bundle.plan.planStepCount > 0 || bundle.mastery.conceptCount > 0 || bundle.wiki.conceptBoundPageCount > 0);
  const wikiSystemBound = bundle.wiki.systemBoundPageCount > 0 ||
    bundle.wiki.partiallyBoundPageCount > 0 ||
    bundle.wiki.diagnosticBoundPageCount > 0 ||
    bundle.wiki.tutorBoundPageCount > 0;
  const questionBankSystemBound = bundle.questionBank.systemBoundEndpointOk &&
    bundle.questionBank.systemBoundCount > 0 &&
    bundle.questionBank.conceptBoundCount > 0 &&
    bundle.questionBank.assessmentItemBoundCount > 0 &&
    bundle.questionBank.diagnosticSourceCount > 0 &&
    bundle.questionBank.systemBoundFilterConsistent &&
    bundle.questionBank.practiceStartOk &&
    bundle.questionBank.practiceReadyCount > 0 &&
    bundle.questionBank.practiceKgBoundCount > 0 &&
    bundle.questionBank.practiceAnswerKeyLeakCount === 0 &&
    bundle.questionBank.practiceSubmitOk &&
    bundle.questionBank.practiceSubmittedCount > 0 &&
    bundle.questionBank.practiceLearningImpactCount > 0;
  const wikiPagePracticeBound = bundle.wiki.pageQuestionsEndpointOk &&
    bundle.wiki.pageQuestionCount > 0 &&
    bundle.wiki.pageQuestionKgBoundCount > 0 &&
    bundle.wiki.pageQuestionAnswerKeyLeakCount === 0 &&
    bundle.wiki.pagePracticeStartOk &&
    bundle.wiki.pagePracticeReadyCount > 0 &&
    bundle.wiki.pagePracticeKgBoundCount > 0 &&
    bundle.wiki.pagePracticeAnswerKeyLeakCount === 0 &&
    bundle.wiki.pagePracticeSubmitOk &&
    bundle.wiki.pagePracticeSubmittedCount > 0 &&
    bundle.wiki.pagePracticeLearningImpactCount > 0;
  passIf(result, "coherence", "id_chain_present", Boolean(ids.topicRef && ids.planRequestRef && ids.quizRunRef), 1, "missing topic/plan/quiz id chain");
  passIf(result, "coherence", "concept_chain_present", conceptAligned || bundle.learningQuality.available, 1, "concept chain unavailable across surfaces");
  passIf(result, "coherence", "wiki_system_binding_present", wikiSystemBound, 1, "wiki content is not bound to plan/diagnostic/tutor signals");
  passIf(result, "coherence", "question_bank_system_bound", questionBankSystemBound, 1, "question bank lacks typed diagnostic/concept binding or practice learning impact", [], true);
  passIf(result, "coherence", "learning_quality_available", bundle.learningQuality.available && !isBadQualityStatus(bundle.learningQuality.qualityStatus), 1, `learning quality status=${bundle.learningQuality.qualityStatus}`);
  passIf(result, "coherence", "wiki_page_practice_bound", wikiPagePracticeBound, 0, "wiki page lacks typed question/practice bridge or learning impact", [], true);
  passIf(result, "coherence", "wiki_reinforcement_available", bundle.wiki.studyCardsEndpointOk && bundle.wiki.recommendationEndpointOk, 0, "wiki study-card/recommendation endpoints unavailable");
}

function applyJudge(includeJudge, judgeResult, result) {
  if (!includeJudge) return;
  if (!judgeResult || judgeResult.status !== "scored") {
    issue(result, "tutor_pedagogy", "judge_unavailable", "optional evaluator judge unavailable", "warning", []);
    return;
  }
  const judgeScore = Number(judgeResult.score);
  if (judgeScore < 0.7) {
    issue(result, "tutor_pedagogy", "judge_low_score", `judge score ${judgeResult.score}`, "warning", judgeResult.evidenceRefs ?? []);
    if (judgeScore < 0.60) {
      result.scores.tutor_pedagogy = Math.min(result.scores.tutor_pedagogy, 18);
    }
  }
  const rubric = judgeResult.rubric ?? {};
  if (Number(rubric.plan_specificity) < 0.75 || Number(rubric.objective_alignment) < 0.75 || Number(rubric.prerequisite_sequence) < 0.75) {
    issue(result, "plan", "content_judge_plan_low", `plan judge rubric below threshold: ${JSON.stringify(compactRubric(rubric))}`, "warning", judgeResult.evidenceRefs ?? []);
    if (judgeScore < 0.60) {
      result.scores.plan = Math.min(result.scores.plan, 15);
    }
  }
  if (Number(rubric.quiz_diagnostic_power) < 0.75 || Number(rubric.misconception_coverage) < 0.75) {
    issue(result, "diagnostic", "content_judge_quiz_low", `quiz judge rubric below threshold: ${JSON.stringify(compactRubric(rubric))}`, "warning", judgeResult.evidenceRefs ?? []);
    if (judgeScore < 0.60) {
      result.scores.diagnostic = Math.min(result.scores.diagnostic, 11);
    }
  }
}

function compactRubric(rubric) {
  return Object.fromEntries(Object.entries(rubric ?? {}).map(([key, value]) => [key, Number(value).toFixed(2)]));
}

function passIf(result, area, code, condition, points, message, evidence = [], critical = false) {
  if (condition) {
    award(result, area, code, points);
    return;
  }
  issue(result, area, code, message, critical ? "critical" : "warning", evidence);
}

function award(result, area, code, points) {
  result.scores[area] = Math.min(SCORE_MAX[area], result.scores[area] + points);
  result.invariants.push({ area, code, status: "pass", points });
}

function issue(result, area, code, message, severity, evidenceRefs) {
  result.invariants.push({ area, code, status: "fail", severity, message, evidenceRefs });
  result.issues.push({ area, code, severity, message, evidenceRefs, deterministic: true });
}

function isPositiveStatus(value) {
  const normalized = String(value ?? "").toLowerCase();
  if (normalized.includes("not_ready") || normalized.includes("failed") || normalized.includes("blocked")) return false;
  return STATUS_OK.has(normalized) || normalized.includes("ready") || normalized.includes("usable");
}

function hasAnyNumber(...values) {
  return values.some((value) => Number.isFinite(Number(value)));
}

function allAtLeast(threshold, ...values) {
  return values.every((value) => Number.isFinite(Number(value)) && Number(value) >= threshold);
}

function planStepCoverageOk(plan) {
  const required = Math.max(1, plan.planStepCount);
  const floor = Math.ceil(required * 0.9);
  return plan.stepObjectiveCount >= floor &&
    plan.stepConceptKeyCount >= floor &&
    plan.stepQuizHookCount >= floor &&
    plan.stepTutorHookCount >= floor &&
    plan.stepWikiHookCount >= floor &&
    plan.stepSuccessCriteriaCount >= floor &&
    plan.stepEvidenceCount >= floor;
}

function isBadQualityStatus(value) {
  const normalized = String(value ?? "").toLowerCase();
  return normalized.includes("failed") || normalized.includes("blocked") || normalized.includes("degraded");
}
