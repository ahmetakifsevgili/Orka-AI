import { expect, test, type Page, type Route } from "@playwright/test";

const topicId = "topic-learning-os-shell";
const user = {
  id: "user-learning-os-shell",
  firstName: "Shell",
  lastName: "Tester",
  email: "shell@orka.local",
};

const sourceWikiPro = {
  topicId,
  readinessStatus: "thin_evidence",
  sourceReadiness: "no_sources",
  wikiReadiness: "empty",
  citationReadiness: "not_ready",
  evidenceMap: {
    uploadedSourceCount: 0,
    readySourceCount: 0,
    wikiPageCount: 0,
    manualNoteCount: 0,
    tutorTraceCount: 0,
    sourceBackedPageCount: 0,
    linkedConceptCount: 0,
    linkedExamOutcomeCount: 0,
    citationWarningCount: 0,
    canClaimSourceGrounded: false,
    providerOutputCountsAsEvidence: false,
    wikiMemoryCountsAsCitationEvidence: false,
  },
  sourceReadinessItems: [],
  wikiReadinessItems: [],
  citationReadinessItems: [],
  linkedConcepts: [],
  linkedExamOutcomes: [],
  sourceBackedConcepts: [],
  sourceLimitedConcepts: [],
  staleSources: [],
  deletedSources: [],
  insufficientSources: [],
  degradedSources: [],
  citationWarnings: [],
  wikiRepairPages: [],
  duplicateTracePages: [],
  manualNotePages: [],
  tutorTracePages: [],
  sourceBackedPages: [],
  notebookPackReadiness: "not_ready",
  todaySourceWikiMission: {
    actionType: "upload_source",
    label: "Attach evidence",
    reason: "No source evidence exists for this topic yet.",
    priority: "medium",
    entryPoint: "notebook",
    targetRoute: "notebook",
    topicId,
    reasonCodes: ["thin_evidence"],
  },
  recommendedActions: [],
  tutorHandoffs: [],
  studyRoomHandoffs: [],
  notebookHandoffs: [],
  examWarRoomWarnings: [],
  missionControlWarnings: [],
  conflictWarnings: [],
  reasonCodes: ["thin_evidence"],
  userSafeSummary: "No source evidence exists yet.",
  generatedAt: "2026-06-05T09:00:00.000Z",
};

const now = "2026-06-05T09:00:00.000Z";

function learningAction(label: string, targetRoute: string, actionType = targetRoute) {
  return {
    actionType,
    label,
    reason: "Shell smoke keeps this as a thin-evidence handoff.",
    priority: "medium",
    entryPoint: targetRoute,
    targetRoute,
    topicId,
    isPrimary: false,
    reasonCodes: ["shell_smoke"],
  };
}

const missionControl = {
  topicId,
  scopeStatus: "thin_evidence",
  primaryMission: {
    missionKey: "shell-primary",
    ...learningAction("Start with Tutor", "tutor", "ask_tutor"),
    isPrimary: true,
  },
  primaryEntryPoint: "tutor",
  secondaryActions: [
    learningAction("Review evidence", "sources-wiki", "source_wiki"),
    learningAction("Open Notebook Studio", "notebook", "notebook_studio"),
  ],
  urgentWarnings: [],
  todayFocus: "Validate the Learning OS shell.",
  reviewLoad: "thin_evidence",
  repairLoad: "thin_evidence",
  examLoad: "limited",
  sourceWikiLoad: "limited",
  studyRoomSuggestion: learningAction("Open Study Room", "study-room", "study_room"),
  moduleCards: [
    "tutor",
    "study-room",
    "review",
    "exams",
    "sources-wiki",
    "notebook",
    "code",
    "progress",
  ].map((view) => ({
    moduleKey: view,
    status: "ready",
    label: view,
    entryPoint: view,
    targetRoute: view,
    priority: "medium",
    userSafeSummary: "Canonical shell mode is available.",
    actionCount: 0,
    warningCount: 0,
    reasonCodes: ["shell_smoke"],
  })),
  sections: [],
  evidenceConfidence: "thin_evidence",
  reasonCodes: ["shell_smoke"],
  userSafeSummary: "Ana Kokpit mevcut öğrenme durumundan okunuyor.",
  generatedAt: now,
};

const studyCoach = {
  topicId,
  scopeStatus: "thin_evidence",
  rhythmStatus: "steady",
  recommendedPace: "short",
  todayPlan: "Use one focused handoff instead of a tool catalogue.",
  weeklyPlan: "Keep evidence and practice loops connected.",
  workload: {
    reviewLoad: "thin_evidence",
    repairLoad: "thin_evidence",
    examLoad: "limited",
    sourceWikiLoad: "limited",
    newLearningLoad: "limited",
    overallLoad: "light",
    loadScore: 0,
  },
  focusPlan: {
    focusMode: "short_loop",
    durationBand: "10-15m",
    entryPoint: "tutor",
    targetRoute: "tutor",
    steps: ["Ask one question", "Check evidence", "Choose a handoff"],
    stopCondition: "No source evidence yet.",
    reasonCodes: ["shell_smoke"],
  },
  comebackPlan: {
    comebackStatus: "not_needed",
    firstStep: "Open Tutor",
    secondStep: "Review evidence",
    avoidToday: "Unproven workload claims",
    reasonCodes: ["shell_smoke"],
    userSafeSummary: "No comeback plan is needed for smoke data.",
  },
  actions: [],
  warnings: [],
  reasonCodes: ["shell_smoke"],
  userSafeSummary: "Coach contract is available.",
  generatedAt: now,
};

const sourceHealth = {
  status: "thin_evidence",
  userSafeLabel: "Thin evidence",
  userSafeDetail: "No uploaded source evidence exists in shell smoke data.",
  citationCoverage: 0,
  unsupportedCitationCount: 0,
};

const learningState = {
  topicId,
  scopeStatus: "thin_evidence",
  signalSummary: {
    evidenceCount: 0,
    quizAttemptCount: 0,
    correctAttemptCount: 0,
    wrongAttemptCount: 0,
    blankOrSkippedAttemptCount: 0,
    dueReviewCount: 0,
    learningSignalCount: 0,
    sourceCount: 0,
    readySourceCount: 0,
    wikiPageCount: 0,
    studyRoomSessionCount: 0,
    studyRoomQuestionCount: 0,
    hasRealLearningData: false,
  },
  sourceHealth,
  longTermLearningProfile: {
    summary: "No durable learning evidence yet.",
    windowDays: 14,
    hasEnoughEvidence: false,
    evidenceCount: 0,
    concepts: [],
    reviewPressure: [],
    weeklyRhythm: {
      todayFocus: "Shell smoke",
      thisWeekFocus: "Collect real signals",
      reviewLoad: "thin_evidence",
      newLearningLoad: "limited",
      repairLoad: "thin_evidence",
      weakConcepts: [],
      dueConcepts: [],
      stableConcepts: [],
      nextBestAction: learningAction("Open Tutor", "tutor", "ask_tutor"),
      reasonCodes: ["shell_smoke"],
      warnings: [],
    },
    nextActions: [],
    reasonCodes: ["shell_smoke"],
    warnings: [],
    generatedAt: now,
  },
  primaryNextAction: {
    ...learningAction("Open Tutor", "tutor", "ask_tutor"),
    source: "mission_control",
    appliesTo: ["tutor"],
  },
  secondaryNextActions: [],
  featureReadiness: [],
  conflictWarnings: [],
  reasonCodes: ["shell_smoke"],
  safetyWarnings: [],
  generatedAt: now,
};

const dashboardToday = {
  dailyFocusTitle: "Ana Kokpit",
  dailyFocusReason: "Use the canonical Learning OS shell.",
  nextAction: {
    label: "Open Tutor",
    reason: "Tutor is the safe first handoff.",
    view: "tutor",
    topicId,
    userSafeStatus: "ready",
  },
  weakConcepts: [],
  sourceHealth,
  dueReviewCount: 0,
  activePlan: null,
  coordinationScope: {
    rootTopicId: topicId,
    currentTopicId: topicId,
    activeLessonTopicId: topicId,
    treeTopicCount: 1,
    sourceCount: 0,
    quizAttemptCount: 0,
    learningSignalCount: 0,
  },
  coordinationHealth: {
    overallStatus: "thin_evidence",
    userSafeSummary: "Shell smoke has no production evidence.",
    windowDays: 7,
    rootTopicId: topicId,
    currentTopicId: topicId,
    activeLessonTopicId: topicId,
    metrics: [],
    generatedAt: now,
  },
  recommendedEntryPoint: {
    view: "tutor",
    label: "Tutor",
    reason: "Start with a single question.",
  },
  orkaLearningState: learningState,
  missionControl,
  studyCoach,
  sourceWikiPro,
  hasRealLearningData: false,
  generatedAt: now,
};

const studyRoom = {
  classroomSessionId: "classroom-shell",
  topicId,
  sessionReadiness: "thin_evidence",
  studyRoomMode: "guided",
  selectedTopic: "Canonical Learning OS Topic",
  sourceReadiness: "no_sources",
  wikiReadiness: "empty",
  rhythmStatus: "steady",
  recommendedPace: "short",
  lessonPlan: {
    planKey: "shell",
    title: "Focused shell check",
    objective: "Keep one guided session available without fake evidence.",
    durationBand: "10-15m",
    steps: ["Confirm context", "Ask one checkpoint", "Return to Tutor"],
    stopCondition: "Evidence is thin.",
    reasonCodes: ["shell_smoke"],
  },
  roles: [
    { roleKey: "student", label: "Student", responsibility: "Answer one focused checkpoint." },
    { roleKey: "tutor", label: "Tutor", responsibility: "Keep the session grounded." },
  ],
  checkpointPlan: {
    checkpointStatus: "ready",
    prompt: "What should the next safe learning handoff be?",
    responseSignal: "needs_review",
    postSubmitFeedback: "Recorded as thin evidence.",
    keyVisible: false,
    reasonCodes: ["shell_smoke"],
  },
  currentTurn: {
    turnStatus: "ready",
    speakerRole: "tutor",
    userSafeSummary: "Study Room can start.",
    responseSignal: "none",
    reasonCodes: ["shell_smoke"],
  },
  safeStudentSummary: "Study Room is available with thin evidence limits.",
  nextActions: [learningAction("Return to Tutor", "tutor", "ask_tutor")],
  tutorHandoffs: [],
  quizHandoffs: [],
  reviewHandoffs: [],
  sourceWikiHandoffs: [],
  notebookHandoffs: [],
  warnings: [],
  reasonCodes: ["shell_smoke"],
  generatedAt: now,
};

const examWarRoom = {
  activeExam: {
    examCode: "kpss",
    displayName: "KPSS",
    verificationStatus: "limited",
    canClaimOfficial: false,
    userSafeVerificationLabel: "Verification limited",
  },
  readinessStatus: "thin_exam_evidence",
  weakSubjects: [],
  weakTopics: [],
  weakOutcomes: [],
  dueOutcomes: [],
  stableOutcomes: [],
  weakQuestionTypes: [],
  denemeMistakeClusters: [],
  practiceReadiness: [],
  todayExamMission: learningAction("Review exam evidence", "exams", "exam_review"),
  weeklyExamPlan: [],
  recommendedPracticeQueue: [],
  tutorRepairHandoffs: [],
  studyRoomHandoffs: [],
  sourceWikiWarnings: [],
  curriculumCoverageWarnings: [],
  conflictWarnings: [],
  reasonCodes: ["shell_smoke"],
  userSafeSummary: "Exam War Room is limited until real attempts exist.",
  generatedAt: now,
};

const notebookStudioPro = {
  topicId,
  readinessStatus: "thin_evidence",
  packReadiness: "not_ready",
  recommendedPacks: [],
  artifactQueue: [],
  exportPreviews: [],
  sourceEvidenceLinks: [],
  wikiEvidenceLinks: [],
  conceptLinks: [],
  examOutcomeLinks: [],
  studyRoomTraceLinks: [],
  tutorHandoffs: [],
  reviewHandoffs: [],
  sourceWikiHandoffs: [],
  examWarRoomHandoffs: [],
  studyRoomHandoffs: [],
  missionControlWarnings: [],
  warnings: [],
  reasonCodes: ["shell_smoke"],
  userSafeSummary: "Notebook Studio is available, but no source-backed pack is ready.",
  generatedAt: now,
};

const codeLearningIde = {
  topicId,
  readinessStatus: "limited",
  mode: "practice",
  activeLanguage: "csharp",
  activeTopic: "Canonical Learning OS Topic",
  runtimeReadiness: {
    status: "limited",
    toolId: "local",
    decision: "editor_available",
    riskLevel: "low",
    timeoutMs: 5000,
    supportedLanguages: ["csharp"],
    warnings: [],
    reasonCodes: ["shell_smoke"],
  },
  session: {
    sessionStatus: "empty",
    signalCount: 0,
    successCount: 0,
    compileErrorCount: 0,
    runtimeErrorCount: 0,
    timeoutCount: 0,
    testFailureCount: 0,
    blankAttemptCount: 0,
  },
  activeExercise: {
    exerciseStatus: "not_started",
    exerciseType: "scratch",
    sourceBasis: "none",
    preSubmitKeyVisible: false,
    reasonCodes: ["shell_smoke"],
  },
  lastAttemptSummary: {
    status: "none",
    phase: "none",
    success: false,
    language: "csharp",
    safeErrorCategory: "none",
    safeTutorSummary: "No code attempt exists yet.",
    durationMs: 0,
    outputTruncated: false,
    reasonCodes: ["shell_smoke"],
  },
  repeatedErrorSummary: {
    dominantErrorType: "none",
    repetitionCount: 0,
    repairSuggestion: "Run one attempt first.",
    reasonCodes: ["shell_smoke"],
  },
  checkpointStatus: "not_started",
  repairStatus: "not_started",
  recommendedActions: [],
  tutorHandoffs: [],
  quizHandoffs: [],
  reviewHandoffs: [],
  wikiHandoffs: [],
  notebookHandoffs: [],
  missionControlWarnings: [],
  runtimeWarnings: [],
  reasonCodes: ["shell_smoke"],
  userSafeSummary: "Code IDE can render with limited runtime context.",
  generatedAt: now,
};

const sourceQuality = {
  id: "source-quality-shell",
  topicId,
  sourceId: null,
  qualityStatus: "no_sources",
  retrievalHealthStatus: "not_applicable",
  citationCoverageStatus: "not_applicable",
  citationSupportStatus: "not_applicable",
  retrievalRunCount: 0,
  emptyRunCount: 0,
  citationCheckCount: 0,
  unsupportedCitationCount: 0,
  citationMissingCount: 0,
  averageContextRelevance: 0,
  citationCoverage: 0,
  evidenceQuality: null,
  recentRetrievalRuns: [],
  recentCitationChecks: [],
  generatedAt: now,
};

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function installShellMocks(page: Page) {
  const consoleErrors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") consoleErrors.push(message.text());
  });
  page.on("pageerror", (error) => consoleErrors.push(error.message));
  page.on("response", (response) => {
    if (response.status() === 404 && response.url().includes("/api/")) {
      const url = new URL(response.url());
      consoleErrors.push(`HTTP 404 ${url.pathname}`);
    }
  });

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace(/^\/api/, "");

    if (path === "/auth/refresh") {
      return fulfillJson(route, { token: "shell-token", user });
    }

    if (path === "/user/me") {
      return fulfillJson(route, user);
    }

    if (path === "/tools/capabilities") {
      return fulfillJson(route, { tools: [], count: 0 });
    }

    if (path === "/tools/runtime/governance-summary") {
      return fulfillJson(route, { deniedCount: 0, degradedCount: 0, warnings: [] });
    }

    if (path === "/learning-runtime/health") {
      return fulfillJson(route, { status: "healthy", warnings: [] });
    }

    if (path === "/dashboard/today") {
      return fulfillJson(route, dashboardToday);
    }

    if (path === "/dashboard/stats") {
      return fulfillJson(route, {});
    }

    if (path === "/quiz/stats") {
      return fulfillJson(route, {});
    }

    if (path === `/quiz/history/${topicId}`) {
      return fulfillJson(route, []);
    }

    if (path === "/user/gamification") {
      return fulfillJson(route, {});
    }

    if (path === "/learning/mission-control") {
      return fulfillJson(route, missionControl);
    }

    if (path === "/learning/study-coach") {
      return fulfillJson(route, studyCoach);
    }

    if (path === "/learning/orka-state") {
      return fulfillJson(route, learningState);
    }

    if (path === "/learning/context-pack") {
      return fulfillJson(route, {
        topicId,
        sessionId: "session-shell",
        scopeStatus: "session",
        estimatedTokenCount: 800,
        blocks: [
          { blockType: "orka_state", status: "ready", summary: "Projection is bounded.", priority: 1, metadata: {} },
          { blockType: "active_lesson_snapshot", status: "ready", summary: "Active lesson snapshot is available.", priority: 2, metadata: {} },
        ],
        warnings: [],
        generatedAt: now,
      });
    }

    if (path === "/learning-snapshots/active-lesson") {
      return fulfillJson(route, {
        id: "active-lesson-shell",
        topicId,
        sessionId: "session-shell",
        activeConceptKey: "projection",
        activeConceptLabel: "Projection binding",
        status: "ready",
        sourceReadiness: "wiki_backed",
        generatedAt: now,
        expiresAt: "2026-06-05T09:20:00.000Z",
      });
    }

    if (path === "/learning-snapshots/student-context") {
      return fulfillJson(route, {
        id: "student-context-shell",
        topicId,
        sessionId: "session-shell",
        sourceReadiness: "wiki_backed",
        confidenceStatus: "thin_evidence",
        generatedAt: now,
        expiresAt: "2026-06-05T09:20:00.000Z",
      });
    }

    if (path === `/plan-quality/topic/${topicId}/readiness`) {
      return fulfillJson(route, { topicId, sessionId: "session-shell", status: "ready", sourceReadiness: "wiki_backed", warnings: [] });
    }

    if (path === `/plan-quality/topic/${topicId}/latest`) {
      return fulfillJson(route, {
        id: "plan-quality-shell",
        topicId,
        sessionId: "session-shell",
        status: "ready",
        planContract: {
          steps: [
            {
              stepId: "step-projection",
              title: "Projection-bound next step",
              objective: "Keep Home, Tutor, Wiki, and Review on the same state.",
              conceptKey: "projection",
              conceptLabel: "Projection binding",
              tutorHook: { tutorMove: "repair" },
              quizHook: { hookType: "checkpoint" },
              evidence: { sourceReadiness: "wiki_backed" },
            },
          ],
        },
      });
    }

    if (path === "/classroom/study-room" || path === "/classroom/study-room/start" || path === "/classroom/study-room/checkpoint") {
      return fulfillJson(route, studyRoom);
    }

    if (path === "/central-exams/kpss/war-room") {
      return fulfillJson(route, examWarRoom);
    }

    if (path === "/notebook-studio/pro") {
      return fulfillJson(route, notebookStudioPro);
    }

    if (path === "/code/learning-ide") {
      return fulfillJson(route, codeLearningIde);
    }

    if (path === "/tutor/next-actions") {
      return fulfillJson(route, []);
    }

    if (path === "/tutor/response-quality/latest" || path.startsWith("/tutor/policy/")) {
      return fulfillJson(route, null);
    }

    if (path === "/learning-artifacts") {
      return fulfillJson(route, { items: [], totalCount: 0 });
    }

    if (path === `/notebook-studio/topic/${topicId}/packs`) {
      return fulfillJson(route, { items: [] });
    }

    if (path === "/topics" && request.method() === "GET") {
      return fulfillJson(route, [
        {
          id: topicId,
          title: "Canonical Learning OS Topic",
          emoji: "O",
          category: "QA",
          parentTopicId: null,
          progressPercentage: 0,
          completedSections: 0,
          order: 0,
          createdAt: "2026-06-05T09:00:00.000Z",
          updatedAt: "2026-06-05T09:00:00.000Z",
        },
      ]);
    }

    if (path === `/topics/${topicId}/sessions/latest`) {
      return fulfillJson(route, { sessionId: "session-shell", topicId, messages: [] });
    }

    if (path === `/wiki/${topicId}`) {
      return fulfillJson(route, []);
    }

    if (path === `/wiki/${topicId}/graph`) {
      return fulfillJson(route, { nodes: [], edges: [] });
    }

    if (path === `/wiki/${topicId}/recommendations`) {
      return fulfillJson(route, { items: [] });
    }

    if (path === `/wiki/${topicId}/workspace-state`) {
      return fulfillJson(route, {
        pinnedPageIds: [],
        openPanels: [],
        activePageId: null,
      });
    }

    if (
      path === `/wiki/${topicId}/glossary` ||
      path === `/wiki/${topicId}/timeline` ||
      path === `/wiki/${topicId}/mindmap` ||
      path === `/wiki/${topicId}/study-cards`
    ) {
      return fulfillJson(route, { items: [] });
    }

    if (path === `/wiki/${topicId}/briefing`) {
      return fulfillJson(route, null, 404);
    }

    if (path.startsWith(`/wiki/${topicId}/`)) {
      return fulfillJson(route, {});
    }

    if (path === `/sources/topic/${topicId}`) {
      return fulfillJson(route, []);
    }

    if (path === `/sources/topic/${topicId}/concept-graph`) {
      return fulfillJson(route, { nodes: [], edges: [] });
    }

    if (path === "/sources/wiki-pro") {
      return fulfillJson(route, sourceWikiPro);
    }

    if (path === "/sources/question-threads") {
      return fulfillJson(route, { items: [] });
    }

    if (path === `/sources/topic/${topicId}/quality`) {
      return fulfillJson(route, sourceQuality);
    }

    if (path === "/sources/study-summary") {
      return fulfillJson(route, null);
    }

    if (path === `/sources/topic/${topicId}/notebook`) {
      return fulfillJson(route, null);
    }

    if (path.startsWith("/sources")) {
      return fulfillJson(route, {});
    }

    if (path === "/flashcards") {
      return fulfillJson(route, []);
    }

    if (path === "/review/due") {
      return fulfillJson(route, []);
    }

    if (path === "/daily-challenge") {
      return fulfillJson(route, null);
    }

    if (path === "/bookmarks") {
      return fulfillJson(route, []);
    }

    if (path === "/question-practice/start" && request.method() === "POST") {
      return fulfillJson(route, {
        practiceSetId: "practice-projection",
        topicId,
        mode: "weak_concept_drill",
        status: "ready",
        totalQuestions: 1,
        questions: [
          {
            questionItemId: "question-projection",
            stem: "Projection binding should update after submit.",
            conceptKey: "projection",
            difficulty: "medium",
            visualReadinessStatus: "ready",
            options: [
              { optionKey: "A", text: "Update projection" },
              { optionKey: "B", text: "Keep stale state" },
            ],
          },
        ],
      });
    }

    if (path === "/question-practice/submit" && request.method() === "POST") {
      return fulfillJson(route, {
        practiceSetId: "practice-projection",
        topicId,
        mode: "weak_concept_drill",
        totalQuestions: 1,
        correctCount: 1,
        wrongCount: 0,
        blankCount: 0,
        results: [
          {
            questionItemId: "question-projection",
            selectedOptionKey: "A",
            isCorrect: true,
            isBlank: false,
            learningImpact: {
              result: "correct",
              nextTutorMove: "continue_plan",
            },
          },
        ],
      });
    }

    return fulfillJson(route, { message: "Shell mock fallback" }, 404);
  });

  await page.addInitScript(
    ({ activeTopicId, activeUser }) => {
      localStorage.setItem("orka_token", "shell-token");
      localStorage.setItem("orka_user", JSON.stringify(activeUser));
      localStorage.setItem("orka_active_topic_id", activeTopicId);
      localStorage.setItem("orka_active_view", "home");
      localStorage.setItem(`orka_premium_tour_seen_v3_${activeUser.id}`, "true");
    },
    { activeTopicId: topicId, activeUser: user },
  );

  return consoleErrors;
}

async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => {
    const root = document.documentElement;
    return {
      body: document.body.scrollWidth - document.body.clientWidth,
      root: root.scrollWidth - root.clientWidth,
    };
  });
  expect(Math.max(overflow.body, overflow.root)).toBeLessThanOrEqual(4);
}

async function clickNav(page: Page, label: string, expectedPath: RegExp) {
  const item = page.getByRole("button", { name: label }).first();
  await expect(item).toBeVisible();
  await item.click();
  await expect(page).toHaveURL(expectedPath);
  await expect(page.locator("body")).toContainText(label);
  await expectNoHorizontalOverflow(page);
}

test.describe("Learning OS Shell Professional Navigation", () => {
  test("keeps learning projection bound across Home, Tutor, Wiki, and Review @projection", async ({ page }) => {
    const apiCalls: Array<{ method: string; path: string; search: string; postData?: string | null }> = [];
    page.on("request", (request) => {
      const url = new URL(request.url());
      if (!url.pathname.startsWith("/api/")) return;
      apiCalls.push({
        method: request.method(),
        path: url.pathname.replace(/^\/api/, ""),
        search: url.search,
        postData: request.postData(),
      });
    });

    const consoleErrors = await installShellMocks(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/app");
    await expect(page).toHaveURL(/\/app/);

    const body = page.locator("body");
    await expect(body).toContainText("Ana Kokpit");
    await expect(body).toContainText("Canonical Learning OS Topic");
    await page.getByRole("button", { name: /Canonical Learning OS Topic/ }).click();
    await expect(page).toHaveURL(/\/app\/tutor$/);
    await clickNav(page, "Ana Kokpit", /\/app$/);
    for (const path of ["/learning/orka-state", "/learning/mission-control", "/learning/study-coach"]) {
      expect(apiCalls.some((call) => call.method === "GET" && call.path === path)).toBeTruthy();
    }

    await clickNav(page, "Tutor", /\/app\/tutor$/);
    await expect(body).toContainText("Orka AI");
    await expect(page.getByText("Bu turda Orka")).toBeVisible();

    await clickNav(page, "Sources / Wiki", /\/app\/sources$/);
    for (const path of ["/learning/context-pack", `/wiki/${topicId}`, "/sources/wiki-pro"]) {
      await expect
        .poll(() => apiCalls.some((call) => call.method === "GET" && call.path === path), {
          message: `missing ${path}; calls=${apiCalls.map((call) => `${call.method} ${call.path}`).join(", ")}`,
        })
        .toBeTruthy();
    }

    await clickNav(page, "Review / Quiz", /\/app\/review$/);
    const orkaStateCountBeforeSubmit = apiCalls.filter((call) => call.path === "/learning/orka-state").length;
    await page.getByRole("button", { name: "Start quiz loop" }).last().click();
    await expect(body).toContainText("Projection binding should update after submit.");
    await page.getByRole("button", { name: /A\s*Update projection/ }).click();
    await page.getByRole("button", { name: "Cevaplari kaydet" }).click();
    await expect(body).toContainText("1/1 dogru");
    await expect.poll(() => apiCalls.filter((call) => call.path === "/learning/orka-state").length).toBeGreaterThan(orkaStateCountBeforeSubmit);
    expect(apiCalls.some((call) => call.method === "POST" && call.path === "/question-practice/submit")).toBeTruthy();

    await clickNav(page, "Ana Kokpit", /\/app$/);
    for (const marker of ["__RAW_PROMPT_CANARY__", "__PROVIDER_PAYLOAD_CANARY__", "__SOURCE_CHUNK_TEXT_CANARY__", "__ANSWER_KEY_CANARY__"]) {
      await expect(body).not.toContainText(marker);
    }
    expect(consoleErrors.filter((message) => !message.includes("favicon"))).toEqual([]);
  });

  test("renders every canonical mode on desktop without legacy IA leakage", async ({ page }) => {
    const consoleErrors = await installShellMocks(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/app");
    await expect(page).toHaveURL(/\/app/);

    for (const [label, path] of [
      ["Ana Kokpit", /\/app$/],
      ["Tutor", /\/app\/tutor$/],
      ["Study Room", /\/app\/study-room$/],
      ["Review / Quiz", /\/app\/review$/],
      ["Exam War Room", /\/app\/exams$/],
      ["Sources / Wiki", /\/app\/sources$/],
      ["Notebook Studio", /\/app\/notebook$/],
      ["Code IDE", /\/app\/code$/],
      ["Progress", /\/app\/progress$/],
      ["Settings / Safety", /\/app\/settings$/],
    ] as const) {
      await clickNav(page, label, path);
    }

    const body = page.locator("body");
    await expect(body).not.toContainText("OrkaWiki");
    await expect(body).not.toContainText("OrkaLM");
    await expect(body).not.toContainText("Pratik / Tekrar");
    expect(consoleErrors.filter((message) => !message.includes("favicon"))).toEqual([]);
  });

  test("keeps the shell contained on mobile viewport", async ({ page }) => {
    await installShellMocks(page);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/app");
    await expect(page).toHaveURL(/\/app/);
    await expect(page.getByRole("button", { name: "Orka home" })).toBeVisible();
    await expectNoHorizontalOverflow(page);
  });
});
