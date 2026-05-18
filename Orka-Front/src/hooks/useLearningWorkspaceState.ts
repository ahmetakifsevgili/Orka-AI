import { useCallback, useEffect, useMemo, useState } from "react";
import {
  LearningArtifactsAPI,
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
};

const emptyState = (topicId?: string | null, sessionId?: string | null): LearningWorkspaceState => ({
  topicId: topicId ?? null,
  sessionId: sessionId ?? null,
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

export function useLearningWorkspaceState({
  topicId,
  sessionId,
  metadata,
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
    ].filter(Boolean).join("|"),
    [
      topicId,
      sessionId,
      metadata?.activeLessonSnapshotId,
      metadata?.studentContextSnapshotId,
      metadata?.planQualitySnapshotId,
      metadata?.tutorActionTraceId,
      metadata?.sourceReadiness,
      metadata?.currentPlanStepId,
      metadata?.latestAssessmentMode,
    ],
  );

  const [state, setState] = useState<LearningWorkspaceState>(() => emptyState(topicId, sessionId));

  const load = useCallback(async () => {
    if (!topicId && !sessionId) {
      setState(emptyState(topicId, sessionId));
      return;
    }

    setState((prev) => ({ ...prev, topicId: topicId ?? null, sessionId: sessionId ?? null, isLoading: true }));

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
    ]);

    const recentArtifacts = artifacts?.items?.slice(0, 6) ?? [];
    const currentPlanStep = normalizePlanStep(firstPlanStep(planQuality), metadata);
    const sourceReadiness =
      sourceEvidenceBundle?.evidenceStatus ??
      wikiNotebookStatus?.evidenceStatus ??
      studentContextSnapshot?.sourceReadiness ??
      planReadiness?.sourceReadiness ??
      tutorPolicy?.sourceReadiness ??
      metadata?.sourceReadiness ??
      metadata?.planSourceReadiness ??
      null;
    const safeNextActions = (nextActions?.length ? nextActions : tutorPolicy?.nextActions ?? nextActionsFromMetadata(metadata)).slice(0, 5);

    setState({
      topicId: topicId ?? null,
      sessionId: sessionId ?? null,
      activeLessonSnapshot,
      studentContextSnapshot,
      currentPlanStep,
      planQuality,
      planReadiness,
      tutorPolicy,
      latestAssessmentImpact: null,
      sourceReadiness,
      sourceEvidenceBundle,
      wikiNotebookStatus,
      toolGovernanceSummary,
      runtimeHealth,
      notebookPacks: notebookPacks?.items?.slice(0, 6) ?? [],
      recentArtifacts,
      nextActions: safeNextActions,
      staleWarnings: collectSourceWarnings(sourceEvidenceBundle, wikiNotebookStatus),
      safetyWarnings: collectArtifactWarnings(recentArtifacts),
      isLoading: false,
      lastSyncedAt: new Date().toISOString(),
    });
  }, [topicId, sessionId, metadata]);

  useEffect(() => {
    let cancelled = false;
    void load().then(() => {
      if (cancelled) return;
    });
    return () => {
      cancelled = true;
    };
  }, [load, metadataKey]);

  return state;
}
