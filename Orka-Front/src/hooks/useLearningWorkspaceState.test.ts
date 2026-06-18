import { describe, expect, it } from "vitest";
import { buildLearningWorkspaceState, isLatestWorkspaceRequest } from "./useLearningWorkspaceState";
import { hasUsableCentralProjection } from "@/components/ProductCoherencePanels";
import type {
  LearningArtifactDto,
  OrkaLearningStateDto,
  OrkaMissionControlDto,
  OrkaStudyCoachDto,
} from "@/lib/types";

const now = "2026-06-18T09:00:00.000Z";
const learningStateVersion = "lsv_projection_test";

const mission = {
  learningStateVersion,
  topicId: "topic-projection",
  sessionId: "session-projection",
  scopeStatus: "session",
  primaryMission: {
    missionKey: "repair-primary",
    actionType: "repair_concept",
    label: "Repair weak concept",
    reason: "Diagnostic evidence changed the next step.",
    priority: "high",
    entryPoint: "ask_tutor",
    targetRoute: "chat",
    topicId: "topic-projection",
    conceptKey: "ratios",
    reasonCodes: ["diagnostic_wrong"],
  },
  primaryEntryPoint: "ask_tutor",
  secondaryActions: [],
  urgentWarnings: [],
  todayFocus: "Repair ratios",
  reviewLoad: "medium",
  repairLoad: "high",
  examLoad: "low",
  sourceWikiLoad: "medium",
  moduleCards: [],
  sections: [],
  evidenceConfidence: "observed",
  reasonCodes: ["projection_test"],
  userSafeSummary: "Projection is coherent.",
  generatedAt: now,
} satisfies OrkaMissionControlDto;

const learningState = {
  learningStateVersion,
  topicId: "topic-projection",
  sessionId: "session-projection",
  scopeStatus: "session",
  signalSummary: {
    evidenceCount: 2,
    quizAttemptCount: 1,
    correctAttemptCount: 0,
    wrongAttemptCount: 1,
    blankOrSkippedAttemptCount: 0,
    dueReviewCount: 1,
    learningSignalCount: 2,
    sourceCount: 1,
    readySourceCount: 1,
    wikiPageCount: 1,
    studyRoomSessionCount: 0,
    studyRoomQuestionCount: 0,
    hasRealLearningData: true,
  },
  sourceHealth: {
    status: "wiki_backed",
    userSafeLabel: "Wiki backed",
    userSafeDetail: "Wiki context is available.",
    citationCoverage: 0,
    unsupportedCitationCount: 0,
  },
  longTermLearningProfile: {
    summary: "Needs repair.",
    windowDays: 14,
    hasEnoughEvidence: true,
    evidenceCount: 2,
    concepts: [],
    reviewPressure: [],
    weeklyRhythm: {
      todayFocus: "Repair ratios",
      thisWeekFocus: "Diagnostic repair",
      reviewLoad: "medium",
      newLearningLoad: "low",
      repairLoad: "high",
      weakConcepts: ["ratios"],
      dueConcepts: [],
      stableConcepts: [],
      nextBestAction: {
        actionType: "repair_concept",
        label: "Repair weak concept",
        reason: "Diagnostic evidence changed the next step.",
        priority: "high",
        reasonCodes: ["diagnostic_wrong"],
      },
      reasonCodes: ["diagnostic_wrong"],
      warnings: [],
    },
    nextActions: [],
    reasonCodes: ["projection_test"],
    warnings: [],
    generatedAt: now,
  },
  primaryNextAction: {
    actionType: "repair_concept",
    label: "Repair weak concept",
    reason: "Diagnostic evidence changed the next step.",
    priority: "high",
    topicId: "topic-projection",
    conceptKey: "ratios",
    source: "diagnostic",
    reasonCodes: ["diagnostic_wrong"],
    appliesTo: ["tutor"],
  },
  secondaryNextActions: [],
  featureReadiness: [],
  conflictWarnings: [
    {
      conflictCode: "source_lag",
      severity: "watch",
      userSafeSummary: "Wiki repair trace is still catching up.",
      reasonCodes: ["projection_test"],
    },
  ],
  reasonCodes: ["projection_test"],
  safetyWarnings: ["no_raw_debug_payload"],
  generatedAt: now,
} satisfies OrkaLearningStateDto;

const studyCoach = {
  learningStateVersion,
  topicId: "topic-projection",
  sessionId: "session-projection",
  scopeStatus: "session",
  rhythmStatus: "steady",
  recommendedPace: "short",
  todayPlan: "Repair first.",
  weeklyPlan: "Keep diagnostic evidence active.",
  workload: {
    reviewLoad: "medium",
    repairLoad: "high",
    examLoad: "low",
    sourceWikiLoad: "medium",
    newLearningLoad: "low",
    overallLoad: "medium",
    loadScore: 2,
  },
  focusPlan: {
    focusMode: "repair",
    durationBand: "10-15m",
    entryPoint: "ask_tutor",
    targetRoute: "chat",
    steps: ["Repair", "Checkpoint"],
    stopCondition: "Mini check complete",
    reasonCodes: ["projection_test"],
  },
  comebackPlan: {
    comebackStatus: "not_needed",
    firstStep: "Repair",
    secondStep: "Checkpoint",
    avoidToday: "New topic",
    reasonCodes: ["projection_test"],
    userSafeSummary: "Stay focused.",
  },
  actions: [],
  warnings: [],
  reasonCodes: ["projection_test"],
  userSafeSummary: "Coach is aligned.",
  generatedAt: now,
} satisfies OrkaStudyCoachDto;

describe("buildLearningWorkspaceState", () => {
  it("merges mission, Orka state, study coach, and bounded context-pack into one projection", () => {
    const state = buildLearningWorkspaceState({
      topicId: "topic-projection",
      sessionId: "session-projection",
      missionControl: mission,
      orkaLearningState: learningState,
      studyCoach,
      contextPack: {
        schemaVersion: "orka.learning-context-pack.v1.1",
        learningStateVersion,
        topicId: "topic-projection",
        sessionId: "session-projection",
        scopeStatus: "session",
        contextWatermark: "ctx_projection",
        estimatedTokenCount: 1200,
        blocks: [
          {
            blockType: "orka_state",
            status: "ready",
            summary: "State ready",
            priority: 1,
            metadata: {},
          },
        ],
        warnings: ["context_pack_bounded"],
        trace: {
          schemaVersion: "orka.learning-context-pack.trace.v1",
          tokenBudget: 2000,
          initialEstimatedTokenCount: 1200,
          estimatedTokenCount: 1200,
          selectedBlocks: [
            {
              blockType: "orka_state",
              status: "ready",
              priority: 1,
              estimatedTokenCount: 25,
            },
          ],
          droppedBlocks: [],
          droppedWarnings: [],
        },
        generatedAt: now,
      },
      artifacts: {
        items: [
          {
            id: "artifact-1",
            artifactType: "summary",
            artifactStatus: "ready",
            title: "Safe summary",
            safeContent: "Short safe content",
            renderFormat: "markdown",
            sourceBasis: "wiki_backed",
            safety: { warnings: [] },
            accessibility: { issues: [] },
          } as unknown as LearningArtifactDto,
        ],
      },
    });

    expect(state.topicId).toBe("topic-projection");
    expect(state.sessionId).toBe("session-projection");
    expect(state.learningStateVersion).toBe(learningStateVersion);
    expect(state.missionControl?.primaryMission.label).toBe("Repair weak concept");
    expect(state.orkaLearningState?.sourceHealth.status).toBe("wiki_backed");
    expect(state.studyCoach?.todayPlan).toBe("Repair first.");
    expect(state.contextPack?.estimatedTokenCount).toBe(1200);
    expect(state.sourceReadiness).toBe("wiki_backed");
    expect(state.nextActions[0]).toMatchObject({
      actionType: "repair_concept",
      userSafeLabel: "Repair weak concept",
      priority: "high",
      targetConceptKey: "ratios",
    });
    expect(state.staleWarnings).toContain("context_pack_bounded");
    expect(state.staleWarnings).toContain("Wiki repair trace is still catching up.");
    expect(state.safetyWarnings).toHaveLength(0);
  });

  it("only lets the latest workspace request update projection state", () => {
    expect(isLatestWorkspaceRequest(3, 3)).toBe(true);
    expect(isLatestWorkspaceRequest(4, 3)).toBe(false);
  });

  it("does not let an empty scoped workspace projection suppress fallback loading", () => {
    expect(hasUsableCentralProjection(buildLearningWorkspaceState({ topicId: "topic-projection" }))).toBe(false);
    expect(hasUsableCentralProjection({
      ...buildLearningWorkspaceState({ topicId: "topic-projection" }),
      isLoading: true,
    })).toBe(true);
    expect(hasUsableCentralProjection(buildLearningWorkspaceState({
      topicId: "topic-projection",
      missionControl: mission,
    }))).toBe(true);
  });
});
