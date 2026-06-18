import { expect, test, type Page, type Route } from "@playwright/test";

const topicId = "topic-onboarding-tutor-spec";
const user = {
  id: "user-onboarding-tutor-spec",
  firstName: "Test",
  lastName: "User",
  email: "testuser@orka.local",
};

const now = "2026-06-12T04:00:00.000Z";

function learningAction(label: string, targetRoute: string, actionType = targetRoute) {
  return {
    actionType,
    label,
    reason: "Onboarding and tutor validation handoff.",
    priority: "medium",
    entryPoint: targetRoute,
    targetRoute,
    topicId,
    isPrimary: false,
    reasonCodes: ["test_smoke"],
  };
}

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
  generatedAt: now,
};

const missionControl = {
  topicId,
  scopeStatus: "thin_evidence",
  primaryMission: {
    missionKey: "test-primary",
    ...learningAction("Start with Tutor", "tutor", "ask_tutor"),
    isPrimary: true,
  },
  primaryEntryPoint: "tutor",
  secondaryActions: [
    learningAction("Review evidence", "sources-wiki", "source_wiki"),
    learningAction("Open Notebook Studio", "notebook", "notebook_studio"),
  ],
  urgentWarnings: [],
  todayFocus: "Validate onboarding and tutor flows.",
  reviewLoad: "thin_evidence",
  repairLoad: "thin_evidence",
  examLoad: "limited",
  sourceWikiLoad: "limited",
  studyRoomSuggestion: learningAction("Open Study Room", "study-room", "study_room"),
  moduleCards: [
    "tutor",
    "sources-wiki",
    "notebook",
  ].map((view) => ({
    moduleKey: view,
    status: "ready",
    label: view,
    entryPoint: view,
    targetRoute: view,
    priority: "medium",
    userSafeSummary: "Canonical view is available.",
    actionCount: 0,
    warningCount: 0,
    reasonCodes: ["test_smoke"],
  })),
  sections: [],
  evidenceConfidence: "thin_evidence",
  reasonCodes: ["test_smoke"],
  userSafeSummary: "Ana Kokpit mevcut öğrenme durumundan okunuyor.",
  generatedAt: now,
};

const studyCoach = {
  topicId,
  scopeStatus: "thin_evidence",
  rhythmStatus: "steady",
  recommendedPace: "short",
  todayPlan: "Onboarding test guidance today.",
  weeklyPlan: "Validate navigation and tutoring flows.",
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
    steps: ["Ask one question", "Check evidence"],
    stopCondition: "No source evidence yet.",
    reasonCodes: ["test_smoke"],
  },
  comebackPlan: {
    comebackStatus: "not_needed",
    firstStep: "Open Tutor",
    secondStep: "Review evidence",
    avoidToday: "Unproven workload claims",
    reasonCodes: ["test_smoke"],
    userSafeSummary: "No comeback plan is needed.",
  },
  actions: [],
  warnings: [],
  reasonCodes: ["test_smoke"],
  userSafeSummary: "Coach contract is available.",
  generatedAt: now,
};

const sourceHealth = {
  status: "thin_evidence",
  userSafeLabel: "Thin evidence",
  userSafeDetail: "No uploaded source evidence exists.",
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
      todayFocus: "Test smoke",
      thisWeekFocus: "Validate core features",
      reviewLoad: "thin_evidence",
      newLearningLoad: "limited",
      repairLoad: "thin_evidence",
      weakConcepts: [],
      dueConcepts: [],
      stableConcepts: [],
      nextBestAction: learningAction("Open Tutor", "tutor", "ask_tutor"),
      reasonCodes: ["test_smoke"],
      warnings: [],
    },
    nextActions: [],
    reasonCodes: ["test_smoke"],
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
  reasonCodes: ["test_smoke"],
  safetyWarnings: [],
  generatedAt: now,
};

const dashboardToday = {
  dailyFocusTitle: "Ana Kokpit",
  dailyFocusReason: "Learn, practice, and check.",
  nextAction: {
    label: "Open Tutor",
    reason: "Tutor is the safe first step.",
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
    userSafeSummary: "Test data holds no production evidence.",
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
  classroomSessionId: "classroom-test",
  topicId,
  sessionReadiness: "thin_evidence",
  studyRoomMode: "guided",
  selectedTopic: "Canonical Test Topic",
  sourceReadiness: "no_sources",
  wikiReadiness: "empty",
  rhythmStatus: "steady",
  recommendedPace: "short",
  lessonPlan: {
    planKey: "test",
    title: "Guided test session",
    objective: "Validate study room navigation.",
    durationBand: "10-15m",
    entryPoint: "study-room",
    targetRoute: "study-room",
    steps: ["Read intro", "Solve checkpoint"],
    stopCondition: "Done check.",
    reasonCodes: ["test_smoke"],
  },
  actions: [],
  warnings: [],
  reasonCodes: ["test_smoke"],
  userSafeSummary: "Study Room is ready for testing.",
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
  reasonCodes: ["test_smoke"],
  userSafeSummary: "KPSS War Room is active.",
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
  reasonCodes: ["test_smoke"],
  userSafeSummary: "Notebook Studio is loaded.",
  generatedAt: now,
};

const codeLearningIde = {
  topicId,
  readinessStatus: "limited",
  mode: "practice",
  activeLanguage: "csharp",
  activeTopic: "Canonical Test Topic",
  runtimeReadiness: {
    status: "limited",
    toolId: "local",
    decision: "editor_available",
    riskLevel: "low",
    timeoutMs: 5000,
    supportedLanguages: ["csharp"],
    warnings: [],
    reasonCodes: ["test_smoke"],
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
    reasonCodes: ["test_smoke"],
  },
  lastAttemptSummary: {
    status: "none",
    phase: "none",
    success: false,
    language: "csharp",
    safeErrorCategory: "none",
    safeTutorSummary: "No code attempt exists.",
    durationMs: 0,
    outputTruncated: false,
    reasonCodes: ["test_smoke"],
  },
  repeatedErrorSummary: {
    dominantErrorType: "none",
    repetitionCount: 0,
    repairSuggestion: "Try typing C# code.",
    reasonCodes: ["test_smoke"],
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
  reasonCodes: ["test_smoke"],
  userSafeSummary: "Code IDE is ready.",
  generatedAt: now,
};

const sourceQuality = {
  id: "source-quality-test",
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

async function installMocks(page: Page) {
  const consoleErrors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") consoleErrors.push(message.text());
  });
  page.on("pageerror", (error) => consoleErrors.push(error.message));

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace(/^\/api/, "");

    if (path === "/auth/refresh") {
      return fulfillJson(route, { token: "test-token", user });
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
        sessionId: "session-test",
        scopeStatus: "session",
        estimatedTokenCount: 640,
        blocks: [
          {
            blockType: "orka_state",
            status: "ready",
            summary: "Tutor projection is available.",
            priority: 1,
            metadata: {},
          },
        ],
        warnings: [],
        generatedAt: now,
      });
    }
    if (path === "/classroom/study-room" || path === "/classroom/study-room/start") {
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
    if (path === "/topics" && request.method() === "GET") {
      return fulfillJson(route, [
        {
          id: topicId,
          title: "Canonical Test Topic",
          emoji: "T",
          category: "QA",
          parentTopicId: null,
          progressPercentage: 10,
          completedSections: 0,
          order: 0,
          createdAt: now,
          updatedAt: now,
        },
      ]);
    }
    if (path === `/topics/${topicId}/sessions/latest`) {
      return fulfillJson(route, { sessionId: "session-test", topicId, messages: [] });
    }
    if (path === "/quiz/plan-diagnostic/intent" && request.method() === "POST") {
      return fulfillJson(route, {
        intentRequestId: "intent-test",
        rawRequest: "Bugun yeni bir konuya baslamak istiyorum. Bana 20 dakikalik sade bir calisma yolu ac.",
        mainTopic: "Onboarding ve Tutor",
        focusArea: "Plan-first giris",
        studyGoal: "Kisa tanilama ile calisma yolu acmak",
        researchIntent: "onboarding tutor plan-first smoke",
        confirmationText: "Once calisma niyetini netlestirdim; onay verirsen arastirma ve seviye testi baslar.",
        language: "tr",
        clarifyingNotes: ["Smoke testi Korteks baslamadan niyet kapisini dogrular."],
        requiresUserConfirmation: true,
      });
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
      return fulfillJson(route, { pinnedPageIds: [], openPanels: [], activePageId: null });
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

    return fulfillJson(route, { message: "Mock Fallback" }, 404);
  });

  return consoleErrors;
}

test.describe("Orka Premium Onboarding & Tutor Validation", () => {
  test("runs the onboarding tour successfully, saves preference, and validates navigation", async ({ page }) => {
    await installMocks(page);

    // Force language to Turkish ("tr") and clear/prepare localStorage
    await page.addInitScript(
      ({ activeUser, activeTopicId }) => {
        localStorage.setItem("orka_token", "test-token");
        localStorage.setItem("orka_user", JSON.stringify(activeUser));
        localStorage.setItem("orka_active_topic_id", activeTopicId);
        localStorage.setItem("orka_active_view", "home");
        localStorage.setItem("orka_language", "tr");
        // Explicitly remove seen value to ensure driver.js triggers
        localStorage.removeItem(`orka_premium_tour_seen_v3_${activeUser.id}`);
      },
      { activeUser: user, activeTopicId: topicId }
    );

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/app");
    await expect(page).toHaveURL(/\/app/);

    // 1. Onboarding Tour triggers
    // Wait for the driver.js popover to appear
    const popover = page.locator(".orka-premium-tour");
    await expect(popover).toBeVisible({ timeout: 10000 });

    // Step 1: "Küçük bir hedefle başla"
    await expect(popover.locator(".driver-popover-title")).toContainText("Küçük bir hedefle başla");
    await popover.getByRole("button", { name: "İleri" }).click();

    // Step 2: "Bugünkü çalışma odağı"
    await expect(popover.locator(".driver-popover-title")).toContainText("Bugünkü çalışma odağı");
    await popover.getByRole("button", { name: "İleri" }).click();

    // Step 3: "Review / Quiz loop"
    await expect(popover.locator(".driver-popover-title")).toContainText("Review / Quiz loop");
    await popover.getByRole("button", { name: "İleri" }).click();

    // Step 4: "Kaynak ve wiki hafızası"
    await expect(popover.locator(".driver-popover-title")).toContainText("Kaynak ve wiki hafızası");
    await popover.getByRole("button", { name: "İleri" }).click();

    // Step 5: "Kod hatası da ders malzemesi"
    await expect(popover.locator(".driver-popover-title")).toContainText("Kod hatası da ders malzemesi");
    await popover.getByRole("button", { name: "Tamam" }).click();

    // Popover should be gone
    await expect(popover).not.toBeVisible();

    // Verify localStorage contains seen = "true"
    const isTourSeen = await page.evaluate((userId) => {
      return localStorage.getItem(`orka_premium_tour_seen_v3_${userId}`);
    }, user.id);
    expect(isTourSeen).toBe("true");

    // 2. Validate Shell Navigation
    // Click Koç (Tutor)
    await page.locator("#tour-nav-learning").click();
    await expect(page).toHaveURL(/\/app\/tutor$/);
    await expect(page.locator("body")).toContainText(/bugün ne çalışacağız/i);

    // Click Wiki
    await page.locator("#tour-nav-wiki").click();
    await expect(page).toHaveURL(/\/app\/sources$/);
    await expect(page.locator("body")).toContainText("Kaynaklar & Wiki");

    // Click OrkaLM
    await page.locator("#tour-nav-ide").click();
    await expect(page).toHaveURL(/\/app\/notebook$/);
    await expect(page.locator("body")).toContainText("Notebook Studio is loaded");

    // Click Planlar
    await page.locator("#tour-nav-dashboard").click();
    await expect(page).toHaveURL(/\/app$/);
    await expect(page.locator("body")).toContainText("Ana Kokpit");
  });

  test("runs an interactive tutoring session and receives streamed responses", async ({ page }) => {
    await installMocks(page);
    let intentRequests = 0;
    let streamRequests = 0;
    let chatMessageRequests = 0;
    const streamBodies: string[] = [];

    await page.route("**/api/quiz/plan-diagnostic/intent", async (route) => {
      intentRequests += 1;
      await fulfillJson(route, {
        intentRequestId: "intent-test",
        rawRequest: "Bugun yeni bir konuya baslamak istiyorum. Bana 20 dakikalik sade bir calisma yolu ac.",
        mainTopic: "Onboarding ve Tutor",
        focusArea: "Plan-first giris",
        studyGoal: "Kisa tanilama ile calisma yolu acmak",
        researchIntent: "onboarding tutor plan-first smoke",
        confirmationText: "Once calisma niyetini netlestirdim; onay verirsen arastirma ve seviye testi baslar.",
        language: "tr",
        clarifyingNotes: ["Smoke testi Korteks baslamadan niyet kapisini dogrular."],
        requiresUserConfirmation: true,
      });
    });

    await page.route("**/api/chat/message", async (route) => {
      chatMessageRequests += 1;
      await fulfillJson(route, { error: "legacy_chat_message_route_should_not_be_used" }, 500);
    });

    // Mock the streaming response of chat API
    await page.route("**/api/chat/stream", async (route) => {
      const request = route.request();
      expect(request.method()).toBe("POST");
      const body = request.postData() ?? "";
      streamBodies.push(body);
      expect(body).toContain("Bana kisa bir basari mesaji ver.");
      expect(body).toContain(topicId);
      expect(body).toContain("session-test");
      streamRequests += 1;

      await route.fulfill({
        status: 200,
        contentType: "text/event-stream",
        headers: {
          "Cache-Control": "no-cache",
          "Connection": "keep-alive",
          "X-Orka-SessionId": "session-test",
        },
        body: [
          `data: [THINKING:Tutor konuyu analiz ediyor...]\n\n`,
          `data: {"type": "token", "content": "Tebrikler! "}\n\n`,
          `data: {"type": "token", "content": "Onboarding ve Tutor "}\n\n`,
          `data: {"type": "token", "content": "testini başarıyla "}\n\n`,
          `data: {"type": "token", "content": "tamamladın."}\n\n`,
          `data: [DONE]\n\n`,
        ].join(""),
      });
    });

    // Set localStorage as authenticated with tour already completed to avoid popups
    await page.addInitScript(
      ({ activeUser, activeTopicId }) => {
        localStorage.setItem("orka_token", "test-token");
        localStorage.setItem("orka_user", JSON.stringify(activeUser));
        localStorage.setItem("orka_active_topic_id", activeTopicId);
        localStorage.setItem("orka_active_view", "tutor");
        localStorage.setItem("orka_language", "tr");
        localStorage.setItem(`orka_premium_tour_seen_v3_${activeUser.id}`, "true");
      },
      { activeUser: user, activeTopicId: topicId }
    );

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/app/tutor");
    await expect(page).toHaveURL(/\/app\/tutor$/);

    // Verify Welcome panel starter buttons are visible
    const starterButton = page.getByRole("button", { name: "Konu öğren" });
    await expect(starterButton).toBeVisible();

    // Click starter button; starter CTAs now stop at the plan intent gate.
    await starterButton.click();
    await expect(page.locator("body")).toContainText("Niyet analizi", { timeout: 15000 });
    expect(intentRequests).toBe(1);
    expect(streamRequests).toBe(0);
    expect(chatMessageRequests).toBe(0);

    const sessionLoad = page
      .waitForResponse(
        (response) => response.url().includes(`/api/topics/${topicId}/sessions/latest`) && response.status() === 200,
        { timeout: 5000 },
      )
      .catch(() => null);
    await page.getByRole("button", { name: /Canonical Test Topic/ }).first().click();
    await sessionLoad;

    // Normal tutor chat still streams after the plan-first gate is visible.
    const chatInput = page.locator("#tour-chat-input");
    await chatInput.fill("Bana kisa bir basari mesaji ver.");
    await chatInput.press("Enter");

    // Message list should contain user prompt and streaming response
    const messageContainer = page.locator("body");
    await expect(messageContainer).toContainText("tamamladın", { timeout: 15000 });
    await expect(messageContainer).toContainText("Tebrikler!");
    await expect(messageContainer).toContainText("Onboarding ve Tutor");
    expect(streamRequests).toBe(1);
    expect(streamBodies).toHaveLength(1);
    expect(chatMessageRequests).toBe(0);
  });
});
