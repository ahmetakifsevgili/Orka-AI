import { array, bool, compactObject, endpointMap, getAny, number, statusText, unique } from "./common.mjs";

export function extractFacts({ scenario, steps, privacy }) {
  const byKey = Object.fromEntries(steps.map((step) => [step.key, step]));
  const diagnosticStart = byKey["diagnostic.start"]?.data ?? {};
  const finalize = byKey["diagnostic.finalize"]?.data ?? {};
  const curriculum = byKey["plan.curriculum"]?.data ?? {};
  const readiness = byKey["plan.readiness"]?.data ?? {};
  const latest = byKey["plan.latest"]?.ok ? byKey["plan.latest"].data : finalize.planQuality ?? {};
  const tutor = byKey["tutor.chat"]?.data ?? {};
  const tutorTrace = byKey["tutor.trace"]?.data ?? {};
  const tutorPolicy = byKey["tutor.policy"]?.data ?? {};
  const pedagogy = byKey["tutor.pedagogy"]?.data ?? {};
  const runtime = byKey["tooling.runtime"]?.data ?? {};
  const wikiPages = array(byKey["wiki.pages"]?.data);
  const wikiDetail = byKey["wiki.page"]?.data ?? {};
  const wikiQuestionSet = byKey["wiki.page.questions"]?.data ?? {};
  const wikiPracticeStart = byKey["wiki.page.practice.start"]?.data ?? {};
  const wikiPracticeSubmit = byKey["wiki.page.practice.submit"]?.data ?? {};
  const wikiQuestions = array(wikiQuestionSet.questions);
  const wikiPracticeQuestions = array(wikiPracticeStart.questions);
  const wikiPracticeResults = array(wikiPracticeSubmit.results);
  const wikiPracticeImpacts = array(wikiPracticeSubmit.learningImpacts);
  const wikiStudyCardsRaw = byKey["wiki.studyCards"]?.data ?? {};
  const wikiRecommendationsRaw = byKey["wiki.recommendations"]?.data ?? {};
  const wikiStudyCards = array(wikiStudyCardsRaw.cards ?? wikiStudyCardsRaw.studyCards ?? wikiStudyCardsRaw.items ?? wikiStudyCardsRaw);
  const wikiRecommendations = array(wikiRecommendationsRaw.recommendations ?? wikiRecommendationsRaw.items ?? wikiRecommendationsRaw.actions ?? wikiRecommendationsRaw);
  const mission = byKey["mission.control"]?.data ?? {};
  const coach = byKey["study.coach"]?.data ?? {};
  const learningQuality = byKey["learning.quality"]?.data ?? {};
  const adaptiveProfile = byKey["mastery.adaptiveProfile"]?.data ?? {};
  const questionBank = array(byKey["questionBank.filter"]?.data);
  const boundQuestionBank = array(byKey["questionBank.systemBound"]?.data);
  const practiceStart = byKey["questionBank.practice.start"]?.data ?? {};
  const practiceSubmit = byKey["questionBank.practice.submit"]?.data ?? {};
  const practiceQuestions = array(practiceStart.questions);
  const practiceResults = array(practiceSubmit.results);
  const practiceImpacts = array(practiceSubmit.learningImpacts);

  const questions = parseQuestions(diagnosticStart.questionsJson);
  const planSteps = array(getAny(latest, ["planContract.steps", "PlanContract.Steps", "planSteps", "steps"], []));
  const chapters = array(curriculum.chapters ?? curriculum.modules);
  const lessons = chapters.flatMap((chapter) => array(chapter.lessons));
  const readyPages = wikiPages.filter((page) => page.contentReadiness === "ready" && page.hasLearningContent === true);
  const degradedPages = wikiPages.filter((page) => page.contentReadiness === "degraded");
  const skeletonPages = wikiPages.filter((page) => page.contentReadiness === "skeleton");
  const traceId = getAny(tutor, ["metadata.tutorActionTraceId", "Metadata.TutorActionTraceId", "tutorActionTraceId"]);
  const trace = tutorTrace.trace ?? {};
  const professional = tutorTrace.professionalContract ?? {};
  const tools = array(tutorTrace.tools).concat(array(runtime.traces));
  const blocks = array(wikiDetail.blocks);
  const wikiBindings = wikiPages.map((page) => page.learningSystemBinding ?? page.LearningSystemBinding).filter(Boolean);
  const blockTypes = unique(blocks.map((block) => block.blockType ?? block.type ?? block.kind));
  const concepts = array(adaptiveProfile.concepts ?? adaptiveProfile.Concepts);
  const rawSourceCount = number(byKey["source.evidenceBundle"]?.data?.sourceCount ?? diagnosticStart.sourceCount);
  const evidenceStatus = statusText(byKey["source.evidenceBundle"]?.data?.evidenceStatus ?? byKey["source.lifecycle"]?.data?.evidenceStatus);
  const wikiBackedCount = evidenceStatus === "wiki_backed" ? Math.max(readyPages.length, blocks.length > 0 ? 1 : 0) : 0;

  return {
    persona: {
      id: scenario.id,
      behavior: scenario.behavior,
      expectRemediation: scenario.expectRemediation,
      blankLearner: scenario.blankLearner,
      sourceSensitive: scenario.sourceSensitive,
    },
    contract: {
      endpoints: endpointMap(steps),
      requiredOkCount: steps.filter((step) => step.required && step.ok && step.parseable).length,
      requiredCount: steps.filter((step) => step.required).length,
      failedRequired: steps.filter((step) => step.required && (!step.ok || !step.parseable)).map((step) => step.key),
    },
    ids: compactObject({
      topicRef: privacy.ref("topic", byKey["topic.create"]?.data?.id),
      sessionRef: privacy.ref("session", tutorTrace.trace?.sessionId ?? trace.sessionId),
      planRequestRef: privacy.ref("plan", diagnosticStart.planRequestId ?? finalize.planRequestId),
      quizRunRef: privacy.ref("quiz", diagnosticStart.quizRunId),
      tutorTraceRef: privacy.ref("trace", traceId ?? trace.id),
      planQualityRef: privacy.ref("planq", readiness.latestQualitySnapshotId ?? latest.id),
      sourceBundleRef: diagnosticStart.sourceBundleHash ? privacy.ref("src", diagnosticStart.sourceBundleHash) : null,
    }),
    intent: {
      hasIntentId: Boolean(byKey["intent.analysis"]?.data?.intentRequestId),
      hasRequiredFields: Boolean(byKey["intent.analysis"]?.data?.mainTopic && byKey["intent.analysis"]?.data?.focusArea && byKey["intent.analysis"]?.data?.studyGoal),
      requiresConfirmation: byKey["intent.analysis"]?.data?.requiresUserConfirmation,
    },
    diagnostic: {
      planRequestPresent: Boolean(diagnosticStart.planRequestId),
      quizRunPresent: Boolean(diagnosticStart.quizRunId),
      questionCount: questions.length,
      idsStable: questions.length > 0 && questions.every(questionId) && unique(questions.map(questionId)).length === questions.length,
      stemOptionComplete: questions.filter((q) => questionStem(q).length >= 12 && questionOptions(q).length >= 3).length,
      conceptTagCount: questions.filter(conceptTag).length,
      conceptDiversity: unique(questions.map(conceptTag)).length,
      difficultyDiversity: unique(questions.map((q) => q.difficulty ?? q.level ?? q.bloomLevel)).length,
      questionTypeDiversity: unique(questions.map((q) => q.questionType ?? q.type ?? q.cognitiveType)).length,
      cognitiveSkillDiversity: unique(questions.map((q) => q.cognitiveSkill ?? q.cognitiveType ?? q.questionType ?? q.type)).length,
      misconceptionProbeCount: questions.filter((q) => q.misconceptionTarget ?? q.expectedMisconceptionCategory).length,
      evidenceExpectedCount: questions.filter((q) => q.evidenceExpected).length,
      scoringRuleCount: questions.filter((q) => q.scoringRule).length,
      learningOutcomeLinkCount: questions.filter((q) => array(q.learningOutcomeIds ?? q.learningOutcomes ?? q.outcomes).length > 0).length,
      answeredCount: steps.filter((step) => step.key.startsWith("diagnostic.attempt.") && step.ok).length,
      blankCount: steps.filter((step) => step.meta?.wasSkipped).length,
      sourceCount: number(diagnosticStart.sourceCount),
      groundingMode: statusText(diagnosticStart.groundingMode),
      conceptGraphQualityStatus: statusText(diagnosticStart.conceptGraphQualityStatus),
      assessmentQualityStatus: statusText(diagnosticStart.assessmentQualityStatus),
      korteksSynthesisStatus: statusText(diagnosticStart.korteksSynthesisStatus),
      learningImpact: steps.findLast?.((step) => step.key.startsWith("diagnostic.attempt.") && step.data?.learningImpact)?.data?.learningImpact,
    },
    plan: {
      generated: bool(finalize.planGenerated),
      chapterCount: number(curriculum.chapterCount, chapters.length),
      lessonCount: number(curriculum.lessonCount, lessons.length),
      isMaterialized: bool(curriculum.isMaterialized),
      qualityStatus: statusText(latest.qualityStatus ?? latest.status ?? finalize.planQuality?.qualityStatus),
      specificityScore: nullableNumber(latest.specificityScore),
      sequencingScore: nullableNumber(latest.sequencingScore),
      evidenceAlignmentScore: nullableNumber(latest.evidenceAlignmentScore),
      assessmentAlignmentScore: nullableNumber(latest.assessmentAlignmentScore),
      tutorAlignmentScore: nullableNumber(latest.tutorAlignmentScore),
      blockingIssueCount: array(latest.blockingIssues).length,
      warningIssueCount: array(latest.warningIssues).length,
      planStepCount: planSteps.length,
      stepObjectiveCount: planSteps.filter((step) => String(step.objective ?? step.Objective ?? "").trim().length >= 12).length,
      stepConceptKeyCount: planSteps.filter((step) => String(step.conceptKey ?? step.ConceptKey ?? "").trim().length > 0).length,
      stepQuizHookCount: planSteps.filter((step) => hasNestedHook(step, "quizHook", "QuizHook", "hookType", "HookType")).length,
      stepTutorHookCount: planSteps.filter((step) => hasNestedHook(step, "tutorHook", "TutorHook", "tutorMove", "TutorMove")).length,
      stepWikiHookCount: planSteps.filter((step) => step.wikiHook || step.WikiHook).length,
      stepSuccessCriteriaCount: planSteps.filter((step) => array(step.successCriteria ?? step.SuccessCriteria).length > 0).length,
      stepEvidenceCount: planSteps.filter((step) => step.evidence || step.Evidence).length,
      readinessStatus: statusText(readiness.planReadinessStatus ?? readiness.readinessStatus),
      sourceReadiness: statusText(readiness.sourceReadiness),
      learnerEvidenceStatus: statusText(readiness.learnerEvidenceStatus),
      repairLoopCount: number(getAny(readiness, ["coursePlanQuality.repairLoopCount"], 0)),
      checkpointCoverage: getAny(readiness, ["coursePlanQuality.checkpointCoverage"]),
    },
    mastery: {
      hasProfile: byKey["mastery.adaptiveProfile"]?.ok === true,
      hasEnoughEvidence: adaptiveProfile.hasEnoughEvidence,
      evidenceCount: number(adaptiveProfile.evidenceCount),
      conceptCount: concepts.length,
      blankOrSkippedCount: concepts.reduce((total, concept) => total + number(concept.blankOrSkippedCount), 0),
      repairCount: concepts.reduce((total, concept) => total + number(concept.repairCount), 0),
      weakConceptCount: concepts.filter((concept) => number(concept.masteryProbability, 1) < 0.55 || /medium|high/i.test(String(concept.remediationNeed))).length,
    },
    tutor: {
      answerPresent: String(tutor.content ?? tutor.message ?? "").length > 0,
      answerRef: privacy.evidenceRef(tutor.content ?? tutor.message ?? ""),
      traceIdPresent: Boolean(traceId ?? trace.id),
      professionalContractPresent: Boolean(professional.schemaVersion),
      teachingMode: statusText(trace.teachingMode ?? trace.TeachingMode),
      activeConceptKey: trace.activeConceptKey ?? trace.ActiveConceptKey ?? null,
      directAnswerPolicy: statusText(trace.directAnswerPolicy ?? trace.DirectAnswerPolicy),
      groundingPolicy: statusText(trace.groundingPolicy ?? trace.GroundingPolicy),
      nextCheckPromptPresent: Boolean(trace.nextCheckPrompt ?? trace.NextCheckPrompt),
      usedPlanStepId: professional.usedPlanStepId ?? null,
      usedPlanStepTitle: professional.usedPlanStepTitle ?? null,
      usedPlanTutorMove: statusText(professional.usedPlanTutorMove),
      usedDiagnosticSignal: statusText(professional.usedDiagnosticSignal),
      usedRepairSignal: bool(professional.usedRepairSignal),
      behaviorShiftReason: statusText(professional.behaviorShiftReason),
      lessonDeliveryMode: statusText(professional.lessonDeliveryMode),
      learnerLevel: statusText(professional.learnerLevel),
      remediationRepairType: statusText(professional.remediationRepairType),
      remediationTriggerType: statusText(professional.remediationTriggerType),
      selectedToolAction: statusText(professional.selectedToolAction),
      degradedFallbackApplied: bool(professional.degradedFallbackApplied),
      toolDecisionReasonCount: number(professional.toolDecisionSummary?.reasonCodeCount),
      learnerSignalCount: number(professional.toolDecisionSummary?.learnerSignalsUsedCount),
      weakConceptKeyCount: array(professional.usedWeakConceptKeys).length,
      microCheckObserved: bool(professional.microCheckObserved),
      rawPayloadExposed: bool(professional.rawPayloadExposed),
      policyQualityStatus: statusText(tutorPolicy.qualityStatus ?? tutorPolicy.status),
      remediationPolicy: statusText(tutorPolicy.remediationPolicy),
      toolPolicy: statusText(tutorPolicy.toolPolicy),
      contextUseCount: array(tutorPolicy.contextUse).length,
      safetyIssueCount: array(tutorPolicy.safetyIssues).length,
    },
    pedagogy: {
      available: byKey["tutor.pedagogy"]?.ok === true,
      status: statusText(pedagogy.status),
      overallScore: nullableNumber(pedagogy.overallScore),
      hasCriticalViolation: bool(pedagogy.hasCriticalViolation),
      warningCount: number(pedagogy.warningCount),
      rubricScoreCount: array(pedagogy.rubricScores ?? pedagogy.scores).length,
      llmJudgeUsed: bool(pedagogy.llmJudgeUsed),
    },
    tooling: {
      traceCount: tools.length,
      tutorToolCount: tools.filter((tool) => tool.tutorActionTraceId || tool.TutorActionTraceId || tool.caller === "tutor").length,
      successCount: tools.filter((tool) => tool.success === true || tool.status === "ready" || tool.decision === "allow").length,
      deniedOrDegradedCount: tools.filter((tool) => ["deny", "blocked", "degraded"].includes(String(tool.decision ?? tool.status))).length,
      highRiskCount: tools.filter((tool) => /high|critical/i.test(String(tool.riskLevel))).length,
    },
    wiki: {
      pageCount: wikiPages.length,
      readyPageCount: readyPages.length,
      degradedPageCount: degradedPages.length,
      skeletonPageCount: skeletonPages.length,
      visibleBlockCount: wikiPages.reduce((total, page) => total + number(page.visibleBlockCount), 0),
      requiredBlockTypesPresentCount: wikiPages.filter((page) => page.requiredBlockTypesPresent === true).length,
      conceptBoundPageCount: wikiBindings.filter((binding) => binding.hasConceptBinding === true).length,
      planBoundPageCount: wikiBindings.filter((binding) => binding.hasPlanBinding === true).length,
      diagnosticBoundPageCount: wikiBindings.filter((binding) => binding.hasDiagnosticBinding === true).length,
      tutorBoundPageCount: wikiBindings.filter((binding) => binding.hasTutorBinding === true).length,
      systemBoundPageCount: wikiBindings.filter((binding) => String(binding.readiness).toLowerCase() === "bound").length,
      partiallyBoundPageCount: wikiBindings.filter((binding) => String(binding.readiness).toLowerCase() === "partially_bound").length,
      detailBlockCount: blocks.length,
      blockTypes,
      curationStatus: statusText(byKey["wiki.curation"]?.data?.curationStatus ?? byKey["wiki.curation"]?.data?.status),
      copilotPrimaryAction: statusText(byKey["wiki.copilot"]?.data?.primaryAction ?? byKey["wiki.copilot"]?.data?.PrimaryAction),
      pageQuestionsEndpointOk: byKey["wiki.page.questions"]?.ok === true,
      pageQuestionStatus: statusText(wikiQuestionSet.status),
      pageQuestionCount: wikiQuestions.length,
      pageQuestionKgBoundCount: wikiQuestions.filter(hasTypedAssessmentBinding).length,
      pageQuestionAnswerKeyLeakCount: wikiQuestions.flatMap((q) => array(q.options)).filter((option) => option.isCorrect === true).length,
      pagePracticeStartOk: byKey["wiki.page.practice.start"]?.ok === true,
      pagePracticeStatus: statusText(wikiPracticeStart.status),
      pagePracticeReadyCount: wikiPracticeQuestions.length,
      pagePracticeKgBoundCount: wikiPracticeQuestions.filter(hasTypedAssessmentBinding).length,
      pagePracticeAnswerKeyLeakCount: wikiPracticeQuestions.flatMap((q) => array(q.options)).filter((option) => option.isCorrect === true).length,
      pagePracticeSubmitOk: byKey["wiki.page.practice.submit"]?.ok === true,
      pagePracticeSubmittedCount: number(wikiPracticeSubmit.totalQuestions),
      pagePracticeLearningImpactCount: wikiPracticeResults.filter((result) => result.learningImpact).length + wikiPracticeImpacts.length,
      studyCardsEndpointOk: byKey["wiki.studyCards"]?.ok === true,
      studyCardCount: wikiStudyCards.length,
      recommendationEndpointOk: byKey["wiki.recommendations"]?.ok === true,
      recommendationCount: wikiRecommendations.length,
    },
    sourceGrounding: {
      evidenceBundleAvailable: byKey["source.evidenceBundle"]?.ok === true,
      lifecycleAvailable: byKey["source.lifecycle"]?.ok === true,
      sourceCount: rawSourceCount + wikiBackedCount,
      rawSourceCount,
      effectiveEvidenceCount: rawSourceCount + wikiBackedCount,
      wikiBackedCount,
      evidenceStatus,
      canClaimSourceGrounded: byKey["source.evidenceBundle"]?.data?.canClaimSourceGrounded,
      citationWarningCount: number(byKey["source.lifecycle"]?.data?.citationWarningCount),
    },
    mission: {
      missionAvailable: byKey["mission.control"]?.ok === true,
      coachAvailable: byKey["study.coach"]?.ok === true,
      primaryActionType: statusText(mission.primaryMission?.actionType ?? mission.primaryMission?.ActionType),
      evidenceConfidence: statusText(mission.evidenceConfidence),
      conflictWarningCount: array(mission.conflictWarnings).length + array(coach.warnings).length,
      reasonCodeCount: array(mission.reasonCodes).length,
      coachActionCount: array(coach.actions).length,
    },
    questionBank: {
      endpointOk: byKey["questionBank.filter"]?.ok === true,
      returnedCount: questionBank.length,
      filterConsistent: questionBank.every((q) => String(q.difficulty).toLowerCase() === "medium" && /multiple/i.test(String(q.questionType ?? q.type))),
      systemBoundEndpointOk: byKey["questionBank.systemBound"]?.ok === true,
      systemBoundCount: boundQuestionBank.length,
      conceptBoundCount: boundQuestionBank.filter((q) => Boolean(q.conceptKey ?? q.ConceptKey)).length,
      assessmentItemBoundCount: boundQuestionBank.filter((q) => Boolean(q.assessmentItemId ?? q.AssessmentItemId)).length,
      diagnosticSourceCount: boundQuestionBank.filter((q) => String(q.questionBankSource ?? q.QuestionBankSource).toLowerCase() === "diagnostic_assessment_item").length,
      systemBoundFilterConsistent: boundQuestionBank.every((q) =>
        String(q.questionBankSource ?? q.QuestionBankSource).toLowerCase() === "diagnostic_assessment_item" &&
        Boolean(q.conceptKey ?? q.ConceptKey)),
      practiceStartOk: byKey["questionBank.practice.start"]?.ok === true,
      practiceStatus: statusText(practiceStart.status),
      practiceReadyCount: practiceQuestions.length,
      practiceKgBoundCount: practiceQuestions.filter((q) =>
        Boolean(q.conceptGraphSnapshotId ?? q.ConceptGraphSnapshotId) &&
        Boolean(q.learningConceptId ?? q.LearningConceptId) &&
        Boolean(q.assessmentItemId ?? q.AssessmentItemId)).length,
      practiceAnswerKeyLeakCount: practiceQuestions.flatMap((q) => array(q.options)).filter((option) => option.isCorrect === true).length,
      practiceSubmitOk: byKey["questionBank.practice.submit"]?.ok === true,
      practiceSubmittedCount: number(practiceSubmit.totalQuestions),
      practiceLearningImpactCount: practiceResults.filter((result) => result.learningImpact).length + practiceImpacts.length,
      practiceBlankCount: number(practiceSubmit.blankCount),
    },
    learningQuality: {
      available: byKey["learning.quality"]?.ok === true,
      qualityStatus: statusText(learningQuality.qualityStatus),
      graphQualityStatus: statusText(learningQuality.graphQualityStatus),
      assessmentQualityStatus: statusText(learningQuality.assessmentQualityStatus),
      masteryConfidenceStatus: statusText(learningQuality.masteryConfidenceStatus),
      tutorPolicyComplianceStatus: statusText(learningQuality.tutorPolicyComplianceStatus),
      sourceGroundingStatus: statusText(learningQuality.sourceGroundingStatus),
      tutorPedagogyStatus: statusText(learningQuality.tutorPedagogyStatus),
      traceHealth: statusText(learningQuality.traceHealth),
      itemBankHealth: statusText(learningQuality.itemBankHealth),
    },
    contentReview: {
      moduleTitles: chapters.map((chapter) => String(chapter.title ?? chapter.name ?? "").slice(0, 120)).filter(Boolean).slice(0, 10),
      lessonTitles: lessons.map((lesson) => String(lesson.title ?? lesson.name ?? "").slice(0, 120)).filter(Boolean).slice(0, 32),
      quizItems: questions.slice(0, 25).map((question) => ({
        ref: privacy.evidenceRef(questionId(question) ?? questionStem(question)),
        stem: questionStem(question).slice(0, 520),
        concept: String(conceptTag(question) ?? "").slice(0, 80),
        difficulty: String(question.difficulty ?? question.level ?? question.bloomLevel ?? "").slice(0, 40),
        questionType: String(question.questionType ?? question.type ?? "").slice(0, 60),
        optionCount: questionOptions(question).length,
        hasMisconceptionTarget: Boolean(question.misconceptionTarget ?? question.expectedMisconceptionCategory),
      })),
      planReadinessStatus: statusText(readiness.planReadinessStatus ?? readiness.readinessStatus),
      planQualityStatus: statusText(latest.qualityStatus ?? latest.status ?? finalize.planQuality?.qualityStatus),
    },
  };
}

function parseQuestions(value) {
  if (Array.isArray(value)) return value;
  try {
    const parsed = JSON.parse(String(value ?? "[]"));
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function questionId(q) {
  return q?.id ?? q?.questionId ?? q?.assessmentItemId ?? null;
}

function questionStem(q) {
  return String(q?.stem ?? q?.question ?? q?.prompt ?? q?.text ?? "");
}

function questionOptions(q) {
  return array(q?.options);
}

function conceptTag(q) {
  return q?.conceptTag ?? q?.conceptKey ?? q?.skillTag ?? q?.learningObjective ?? "";
}

function hasNestedHook(step, camelName, pascalName, camelField, pascalField) {
  const hook = step?.[camelName] ?? step?.[pascalName];
  if (!hook || typeof hook !== "object") return false;
  return String(hook[camelField] ?? hook[pascalField] ?? "").trim().length > 0;
}

function hasTypedAssessmentBinding(question) {
  return Boolean(question?.conceptGraphSnapshotId ?? question?.ConceptGraphSnapshotId) &&
    Boolean(question?.learningConceptId ?? question?.LearningConceptId) &&
    Boolean(question?.assessmentItemId ?? question?.AssessmentItemId) &&
    Boolean(question?.conceptKey ?? question?.ConceptKey);
}

function nullableNumber(value) {
  if (value === null || value === undefined || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}
