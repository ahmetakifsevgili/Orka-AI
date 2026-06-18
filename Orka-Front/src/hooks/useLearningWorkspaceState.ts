import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  LearningArtifactsAPI,
  LearningAPI,
  LearningSnapshotsAPI,
  LearningRuntimeAPI,
  NotebookStudioAPI,
  PlanQualityAPI,
  SourcesAPI,
  ToolsAPI,
  TutorAPI,
  WikiAPI,
} from "@/services/api";
import type {
  ChatResponseMetadata,
  LearningWorkspaceCurrentPlanStep,
  LearningWorkspaceState,
  LearningContextPackDto,
  OrkaLearningStateDto,
  OrkaMissionControlDto,
  OrkaStudyCoachDto,
  PlanQualityEvaluationDto,
  PlanStepContractDto,
  SourceEvidenceBundleDto,
  TutorNextLearningActionDto,
  WikiKnowledgeNotebookDto,
} from "@/lib/types";

type WorkspaceStateOptions = {
  topicId?: string | null;
  sessionId?: string | null;
  metadata?: ChatResponseMetadata | null;
  includeContextPack?: boolean;
  refreshKey?: string | number | null;
};

export type BuildLearningWorkspaceStateInput = {
  topicId?: string | null;
  sessionId?: string | null;
  metadata?: ChatResponseMetadata | null;
  activeLessonSnapshot?: LearningWorkspaceState["activeLessonSnapshot"];
  studentContextSnapshot?: LearningWorkspaceState["studentContextSnapshot"];
  planReadiness?: LearningWorkspaceState["planReadiness"];
  planQuality?: LearningWorkspaceState["planQuality"];
  tutorPolicy?: LearningWorkspaceState["tutorPolicy"];
  nextActions?: TutorNextLearningActionDto[] | null;
  sourceEvidenceBundle?: SourceEvidenceBundleDto | null;
  wikiNotebookStatus?: WikiKnowledgeNotebookDto | null;
  artifacts?: { items?: LearningWorkspaceState["recentArtifacts"] | null } | null;
  notebookPacks?: { items?: LearningWorkspaceState["notebookPacks"] | null } | null;
  toolGovernanceSummary?: LearningWorkspaceState["toolGovernanceSummary"];
  runtimeHealth?: LearningWorkspaceState["runtimeHealth"];
  contextPack?: LearningContextPackDto | null;
  orkaLearningState?: OrkaLearningStateDto | null;
  missionControl?: OrkaMissionControlDto | null;
  studyCoach?: OrkaStudyCoachDto | null;
};

const emptyState = (topicId?: string | null, sessionId?: string | null): LearningWorkspaceState => ({
  topicId: topicId ?? null,
  sessionId: sessionId ?? null,
  contextPack: null,
  orkaLearningState: null,
  missionControl: null,
  studyCoach: null,
  activeLessonSnapshot: null,
  studentContextSnapshot: null,
  currentPlanStep: null,
  planQuality: null,
  planReadiness: null,
  tutorPolicy: null,
  latestAssessmentImpact: null,
  sourceReadiness: null,
  sourceEvidenceBundle: null,
  wikiNotebookStatus: null,
  toolGovernanceSummary: null,
  runtimeHealth: null,
  notebookPacks: [],
  recentArtifacts: [],
  nextActions: [],
  staleWarnings: [],
  safetyWarnings: [],
  isLoading: false,
  lastSyncedAt: null,
});

async function quiet<T>(promise: Promise<T>): Promise<T | null> {
  try {
    return await promise;
  } catch {
    return null;
  }
}

function firstPlanStep(planQuality?: PlanQualityEvaluationDto | null): PlanStepContractDto | null {
  return planQuality?.planContract?.steps?.[0] ?? null;
}

function planStepFromMetadata(metadata?: ChatResponseMetadata | null): LearningWorkspaceCurrentPlanStep | null {
  if (!metadata?.currentPlanStepId && !metadata?.currentPlanStepTitle && !metadata?.activePlanStepId) return null;
  return {
    id: metadata.currentPlanStepId ?? metadata.activePlanStepId ?? null,
    title: metadata.currentPlanStepTitle ?? null,
    conceptKey: metadata.activeConceptKey ?? null,
    tutorMove: metadata.currentPlanTutorMove ?? metadata.tutorTeachingMove ?? metadata.teachingMode ?? null,
    quizHook: metadata.currentPlanQuizHook ?? metadata.latestAssessmentMode ?? null,
    sourceReadiness: metadata.planSourceReadiness ?? metadata.sourceReadiness ?? null,
  };
}

function normalizePlanStep(
  step?: PlanStepContractDto | null,
  metadata?: ChatResponseMetadata | null,
): LearningWorkspaceCurrentPlanStep | null {
  if (!step) return planStepFromMetadata(metadata);
  return {
    id: step.stepId,
    title: step.title,
    objective: step.objective,
    conceptKey: step.conceptKey,
    conceptLabel: step.conceptLabel,
    sequenceReason: step.sequenceReason,
    tutorMove: step.tutorHook?.tutorMove ?? metadata?.tutorTeachingMove ?? null,
    quizHook: step.quizHook?.hookType ?? metadata?.latestAssessmentMode ?? null,
    sourceReadiness: step.evidence?.sourceReadiness ?? metadata?.sourceReadiness ?? null,
    fallbackIfEvidenceWeak: step.fallbackIfEvidenceWeak,
  };
}

function collectSourceWarnings(
  bundle?: SourceEvidenceBundleDto | null,
  notebook?: WikiKnowledgeNotebookDto | null,
): string[] {
  const warnings = new Set<string>();
  bundle?.warnings?.forEach((warning) => warnings.add(warning));
  notebook?.sourceWarnings?.forEach((warning) => warnings.add(warning));
  if ((bundle?.staleEvidenceCount ?? 0) > 0) warnings.add("source evidence stale");
  if ((bundle?.deletedEvidenceCount ?? 0) > 0) warnings.add("deleted source evidence ignored");
  if (bundle?.evidenceStatus && ["degraded", "stale", "evidence_insufficient"].includes(bundle.evidenceStatus)) {
    warnings.add(bundle.evidenceStatus);
  }
  return Array.from(warnings).slice(0, 6);
}

function collectArtifactWarnings(artifacts: LearningWorkspaceState["recentArtifacts"]): string[] {
  const warnings = new Set<string>();
  artifacts.forEach((artifact) => {
    artifact.safety?.warnings?.forEach((warning) => warnings.add(warning));
    artifact.accessibility?.issues?.forEach((issue) => warnings.add(issue));
    if (["degraded", "stale"].includes(artifact.artifactStatus)) warnings.add(`${artifact.artifactType}: ${artifact.artifactStatus}`);
  });
  return Array.from(warnings).slice(0, 6);
}

function nextActionsFromMetadata(metadata?: ChatResponseMetadata | null): TutorNextLearningActionDto[] {
  return (metadata?.tutorNextLearningActions ?? []).slice(0, 5).map((label, index) => ({
    actionType: "metadata_next_action",
    userSafeLabel: label,
    priority: index === 0 ? "high" : "normal",
  }));
}

export function isLatestWorkspaceRequest(latestRequestId: number, requestId: number): boolean {
  return latestRequestId === requestId;
}

function nextActionsFromProjection(
  missionControl?: OrkaMissionControlDto | null,
  orkaLearningState?: OrkaLearningStateDto | null,
): TutorNextLearningActionDto[] {
  const projected: TutorNextLearningActionDto[] = [];
  const push = (action?: {
    actionType?: string | null;
    label?: string | null;
    priority?: string | null;
    conceptKey?: string | null;
  } | null) => {
    if (!action?.label && !action?.actionType) return;
    projected.push({
      actionType: action.actionType ?? "projection_next_action",
      userSafeLabel: action.label ?? action.actionType ?? "Next learning step",
      targetConceptKey: action.conceptKey ?? null,
      priority: action.priority ?? "normal",
    });
  };

  push(missionControl?.primaryMission);
  (missionControl?.secondaryActions ?? []).forEach(push);
  push(orkaLearningState?.primaryNextAction);
  (orkaLearningState?.secondaryNextActions ?? []).forEach(push);

  const seen = new Set<string>();
  return projected.filter((action) => {
    const key = `${action.actionType}|${action.userSafeLabel}|${action.targetConceptKey ?? ""}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

export function buildLearningWorkspaceState(input: BuildLearningWorkspaceStateInput): LearningWorkspaceState {
  const recentArtifacts = input.artifacts?.items?.slice(0, 6) ?? [];
  const currentPlanStep = normalizePlanStep(firstPlanStep(input.planQuality), input.metadata);
  const sourceReadiness =
    input.sourceEvidenceBundle?.evidenceStatus ??
    input.wikiNotebookStatus?.evidenceStatus ??
    input.orkaLearningState?.sourceHealth?.status ??
    input.studentContextSnapshot?.sourceReadiness ??
    input.planReadiness?.sourceReadiness ??
    input.tutorPolicy?.sourceReadiness ??
    input.metadata?.sourceReadiness ??
    input.metadata?.planSourceReadiness ??
    null;
  const projectedNextActions = nextActionsFromProjection(input.missionControl, input.orkaLearningState);
  const safeNextActions = (
    input.nextActions?.length
      ? input.nextActions
      : input.tutorPolicy?.nextActions?.length
        ? input.tutorPolicy.nextActions
        : projectedNextActions.length
          ? projectedNextActions
          : nextActionsFromMetadata(input.metadata)
  ).slice(0, 5);

  return {
    topicId: input.topicId ?? input.orkaLearningState?.topicId ?? input.missionControl?.topicId ?? input.studyCoach?.topicId ?? null,
    sessionId: input.sessionId ?? input.orkaLearningState?.sessionId ?? input.missionControl?.sessionId ?? input.studyCoach?.sessionId ?? null,
    contextPack: input.contextPack ?? null,
    orkaLearningState: input.orkaLearningState ?? null,
    missionControl: input.missionControl ?? null,
    studyCoach: input.studyCoach ?? null,
    activeLessonSnapshot: input.activeLessonSnapshot ?? null,
    studentContextSnapshot: input.studentContextSnapshot ?? null,
    currentPlanStep,
    planQuality: input.planQuality ?? null,
    planReadiness: input.planReadiness ?? null,
    tutorPolicy: input.tutorPolicy ?? null,
    latestAssessmentImpact: null,
    sourceReadiness,
    sourceEvidenceBundle: input.sourceEvidenceBundle ?? null,
    wikiNotebookStatus: input.wikiNotebookStatus ?? null,
    toolGovernanceSummary: input.toolGovernanceSummary ?? null,
    runtimeHealth: input.runtimeHealth ?? null,
    notebookPacks: input.notebookPacks?.items?.slice(0, 6) ?? [],
    recentArtifacts,
    nextActions: safeNextActions,
    staleWarnings: [
      ...collectSourceWarnings(input.sourceEvidenceBundle, input.wikiNotebookStatus),
      ...(input.contextPack?.warnings ?? []),
      ...(input.orkaLearningState?.safetyWarnings ?? []),
      ...(input.orkaLearningState?.conflictWarnings ?? []).map((warning) => warning.userSafeSummary),
    ].filter(Boolean).slice(0, 8),
    safetyWarnings: collectArtifactWarnings(recentArtifacts),
    isLoading: false,
    lastSyncedAt: new Date().toISOString(),
  };
}

export function useLearningWorkspaceState({
  topicId,
  sessionId,
  metadata,
  includeContextPack = false,
  refreshKey = null,
}: WorkspaceStateOptions): LearningWorkspaceState {
  const metadataKey = useMemo(
    () => [
      metadata?.activeLessonSnapshotId,
      metadata?.studentContextSnapshotId,
      metadata?.planQualitySnapshotId,
      metadata?.tutorActionTraceId,
      metadata?.sourceReadiness,
      metadata?.currentPlanStepId,
      metadata?.latestAssessmentMode,
      includeContextPack,
      refreshKey,
    ].filter(Boolean).join("|"),
    [
      topicId,
      sessionId,
      includeContextPack,
      metadata?.activeLessonSnapshotId,
      metadata?.studentContextSnapshotId,
      metadata?.planQualitySnapshotId,
      metadata?.tutorActionTraceId,
      metadata?.sourceReadiness,
      metadata?.currentPlanStepId,
      metadata?.latestAssessmentMode,
      refreshKey,
    ],
  );

  const [state, setState] = useState<LearningWorkspaceState>(() => emptyState(topicId, sessionId));
  const latestRequestIdRef = useRef(0);

  const load = useCallback(async (requestId: number) => {
    if (!topicId && !sessionId) {
      if (isLatestWorkspaceRequest(latestRequestIdRef.current, requestId)) {
        setState(emptyState(topicId, sessionId));
      }
      return;
    }

    if (isLatestWorkspaceRequest(latestRequestIdRef.current, requestId)) {
      setState((prev) => ({ ...prev, topicId: topicId ?? null, sessionId: sessionId ?? null, isLoading: true }));
    }

    const snapshotParams = { topicId: topicId ?? undefined, sessionId: sessionId ?? undefined };
    const [
      activeLessonSnapshot,
      studentContextSnapshot,
      planReadiness,
      planQuality,
      tutorPolicy,
      nextActions,
      sourceEvidenceBundle,
      wikiNotebookStatus,
      artifacts,
      notebookPacks,
      toolGovernanceSummary,
      runtimeHealth,
      orkaLearningState,
      missionControl,
      studyCoach,
      contextPack,
    ] = await Promise.all([
      quiet(LearningSnapshotsAPI.getActiveLesson(snapshotParams)),
      quiet(LearningSnapshotsAPI.getStudentContext(snapshotParams)),
      topicId ? quiet(PlanQualityAPI.getReadiness(topicId, sessionId ?? undefined)) : Promise.resolve(null),
      topicId ? quiet(PlanQualityAPI.getLatest(topicId, sessionId ?? undefined)) : Promise.resolve(null),
      topicId ? quiet(TutorAPI.getTopicPolicy(topicId, sessionId ?? undefined)) : sessionId ? quiet(TutorAPI.getSessionPolicy(sessionId)) : Promise.resolve(null),
      quiet(TutorAPI.getNextActions(snapshotParams)),
      topicId ? quiet(SourcesAPI.getEvidenceBundle(topicId, sessionId ?? undefined)) : Promise.resolve(null),
      topicId ? quiet(WikiAPI.getKnowledgeNotebook(topicId)) : Promise.resolve(null),
      quiet(LearningArtifactsAPI.list(snapshotParams)),
      topicId ? quiet(NotebookStudioAPI.listPacks(topicId, sessionId ?? undefined)) : Promise.resolve(null),
      quiet(ToolsAPI.getGovernanceSummary(snapshotParams)),
      quiet(LearningRuntimeAPI.getHealth(snapshotParams)),
      quiet(LearningAPI.getOrkaState(snapshotParams)),
      quiet(LearningAPI.getMissionControl(snapshotParams)),
      quiet(LearningAPI.getStudyCoach(snapshotParams)),
      includeContextPack ? quiet(LearningAPI.getContextPack(snapshotParams)) : Promise.resolve(null),
    ]);

    if (!isLatestWorkspaceRequest(latestRequestIdRef.current, requestId)) {
      return;
    }

    setState(buildLearningWorkspaceState({
      topicId: topicId ?? null,
      sessionId: sessionId ?? null,
      contextPack,
      orkaLearningState,
      missionControl,
      studyCoach,
      activeLessonSnapshot,
      studentContextSnapshot,
      planQuality,
      planReadiness,
      tutorPolicy,
      nextActions,
      sourceEvidenceBundle,
      wikiNotebookStatus,
      toolGovernanceSummary,
      runtimeHealth,
      artifacts,
      notebookPacks,
      metadata,
    }));
  }, [topicId, sessionId, metadataKey, includeContextPack, refreshKey]);

  useEffect(() => {
    const requestId = latestRequestIdRef.current + 1;
    latestRequestIdRef.current = requestId;
    void load(requestId);
    return () => {
      if (isLatestWorkspaceRequest(latestRequestIdRef.current, requestId)) {
        latestRequestIdRef.current += 1;
      }
    };
  }, [load]);

  if ((state.topicId ?? null) !== (topicId ?? null) || (state.sessionId ?? null) !== (sessionId ?? null)) {
    return {
      ...emptyState(topicId, sessionId),
      isLoading: Boolean(topicId || sessionId),
    };
  }

  return state;
}
