import { chromium } from "playwright";
import { spawn } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const outputDir = path.resolve(root, "..", "artifacts", "ui-gallery");
const port = Number(process.env.ORKA_CAPTURE_PORT ?? "4317");
const baseURL = `http://127.0.0.1:${port}`;

const topic = {
  id: "topic-demo",
  title: "Türev ve Grafik Okuma",
  emoji: "∫",
  category: "YKS Matematik",
  progressPercentage: 42,
  isMastered: false,
  createdAt: new Date(Date.now() - 86400000 * 4).toISOString(),
  updatedAt: new Date().toISOString(),
};

const user = {
  id: "user-demo",
  email: "demo@orka.local",
  firstName: "Demo",
  lastName: "Öğrenci",
};

const action = (label, targetRoute, reason = "Bu adım mevcut çalışma durumuna göre en güvenli başlangıç.") => ({
  label,
  targetRoute,
  entryPoint: targetRoute,
  actionType: targetRoute,
  priority: "normal",
  reason,
  reasonCodes: ["thin_evidence", "safe_next_step"],
});

const mission = {
  userSafeSummary: "Bugünün odağı tek bir öğrenme adımını netleştirip doğru çalışma alanına geçmek.",
  primaryMission: action("Kısa konu onarımı ile başla", "tutor", "Son çalışmada kavram kanıtı zayıf; önce küçük bir açıklama ve kontrol daha güvenli."),
  studyRoomSuggestion: action("Sınıf anlatımına geç", "study-room", "Chat yerine daha akışlı ders ortamı ile devam edebilirsin."),
  warnings: [
    { warningCode: "thin_evidence", severity: "warning", label: "Kanıt henüz ince", reasonCodes: ["az_soru", "kaynak_yok"] },
  ],
  moduleCards: [
    { moduleKey: "tutor", label: "Tutor", status: "ready", targetRoute: "tutor", entryPoint: "tutor", priority: "normal", userSafeSummary: "Chat ile açıklama, örnek ve küçük kontrol.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "study_room", label: "Study Room", status: "ready", targetRoute: "study-room", entryPoint: "study-room", priority: "normal", userSafeSummary: "Chat yorarsa sınıf hissinde ders anlatımı.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "review", label: "Review / Quiz", status: "ready", targetRoute: "review", entryPoint: "review", priority: "normal", userSafeSummary: "Kısa tekrar ve checkpoint.", actionCount: 2, warningCount: 0, reasonCodes: [] },
    { moduleKey: "exam", label: "Exam War Room", status: "limited", targetRoute: "exams", entryPoint: "exams", priority: "normal", userSafeSummary: "Sınav zayıflıklarını ve deneme izlerini toparlar.", actionCount: 1, warningCount: 1, reasonCodes: [] },
    { moduleKey: "sources", label: "Sources / Wiki", status: "ready", targetRoute: "sources-wiki", entryPoint: "sources-wiki", priority: "normal", userSafeSummary: "Kaynak, citation ve wiki kanıt alanı.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "notebook", label: "Notebook Studio", status: "ready", targetRoute: "notebook", entryPoint: "notebook", priority: "low", userSafeSummary: "Özet, çalışma paketi ve export bağlamı.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "code", label: "Code IDE", status: "limited", targetRoute: "code", entryPoint: "code", priority: "low", userSafeSummary: "Kod pratiği ve hata onarımı.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "progress", label: "Progress", status: "ready", targetRoute: "progress", entryPoint: "progress", priority: "low", userSafeSummary: "Hafıza ve ilerleme özeti.", actionCount: 1, warningCount: 0, reasonCodes: [] },
  ],
  sections: [
    { sectionKey: "repair", label: "Onarım", status: "ready", actions: [action("Tutor ile açıkla", "tutor")], reasonCodes: ["weak_concept"] },
    { sectionKey: "evidence", label: "Kaynak kanıtı", status: "watch", actions: [action("Sources / Wiki aç", "sources-wiki")], reasonCodes: ["source_needed"] },
  ],
};

const studyRoom = {
  topicId: topic.id,
  classroomSessionId: "classroom-demo",
  sessionReadiness: "ready",
  studyRoomMode: "guided_lesson",
  recommendedPace: "calm",
  rhythmStatus: "steady",
  sourceReadiness: "source_limited",
  wikiReadiness: "wiki_ready",
  selectedTopic: topic.title,
  safeStudentSummary: "Chat yerine hoca/asistan akışında kısa ders, örnek ve mini kontrol.",
  lessonPlan: {
    title: "Türev grafikte neyi gösterir?",
    objective: "Grafikte eğim fikrini kısa anlatım ve tek örnekle netleştirmek.",
    steps: ["Tahtada ana fikri dinle", "Bir grafik örneğini beraber oku", "Mini kontrolde takıldığın yeri işaretle"],
    durationBand: "10-15 dakika",
    stopCondition: "Mini kontrol tamamlanınca",
  },
  roles: [
    { roleKey: "teacher", label: "Hoca", responsibility: "Konuyu sade bir sırayla anlatır." },
    { roleKey: "assistant", label: "Asistan", responsibility: "Örneği açar ve takıldığın yeri yakalar." },
    { roleKey: "note_taker", label: "Not tutucu", responsibility: "Ders sonunda 3 kısa not bırakır." },
  ],
  checkpointPlan: {
    checkpointStatus: "ready",
    prompt: "Grafikte türev hangi bilgiye karşılık geliyor?",
    responseSignal: "needs_review",
    keyVisible: false,
    reasonCodes: ["demo"],
  },
  nextActions: [action("Kısa quiz ile kontrol et", "review")],
  tutorHandoffs: [action("Tutor'a soru sor", "tutor")],
  quizHandoffs: [],
  reviewHandoffs: [],
  sourceWikiHandoffs: [],
  notebookHandoffs: [],
  warnings: [],
};

const examWarRoom = {
  activeExam: {
    examCode: "kpss",
    displayName: "KPSS",
    verificationStatus: "limited",
    canClaimOfficial: false,
  },
  readinessStatus: "thin_exam_evidence",
  userSafeSummary: "Sınav hazırlığını zayıf outcome ve deneme izlerine göre toparlar.",
  todayExamMission: action("Paragraf ana fikir zayıflığını onar", "review", "Deneme izinde aynı soru tipi tekrar ediyor."),
  weakOutcomes: [{ label: "Ana fikir", userSafeSummary: "Paragraf sorularında ana fikir işareti zayıf.", recommendedAction: "8 kısa soru çöz", readinessStatus: "watch" }],
  dueOutcomes: [],
  denemeMistakeClusters: [{ label: "Paragraf kümesi", recommendedAction: "Önce strateji, sonra mini deneme.", mistakeCount: 4 }],
  weakQuestionTypes: [{ questionType: "main_idea", recommendedAction: "Ana fikir kalıbını çalış.", readinessStatus: "watch" }],
  recommendedPracticeQueue: [action("8 soruluk telafi çöz", "review")],
  tutorRepairHandoffs: [action("Tutor ile strateji aç", "tutor")],
  studyRoomHandoffs: [action("Sınıf anlatımına geç", "study-room")],
  sourceWikiWarnings: [],
  curriculumCoverageWarnings: [],
  conflictWarnings: [],
};

const sourceWiki = {
  title: "Sources / Wiki",
  userSafeSummary: "Kaynakları, wiki sayfalarını, citation durumunu ve graph bağlamını toplar.",
  sourceReadiness: "ready",
  wikiReadiness: "ready",
  citationReadiness: "limited",
  notebookPackReadiness: "ready",
  evidenceMap: { readySourceCount: 2, uploadedSourceCount: 3, wikiPageCount: 5, citationWarningCount: 1 },
  todaySourceWikiMission: action("Kaynak dayanağını gözden geçir", "sources-wiki"),
  recommendedActions: [action("Wiki sayfasını aç", "sources-wiki")],
  tutorHandoffs: [action("Kaynaktan soru sor", "tutor")],
  studyRoomHandoffs: [action("Kaynaklı dersi başlat", "study-room")],
  notebookHandoffs: [action("Özet paketi çıkar", "notebook")],
  sourceWarnings: [],
  wikiWarnings: [],
  citationWarnings: [],
  staleSources: [{ title: "Türev notu", pageCount: 8, linkedConceptCount: 2, sourceReadiness: "watch" }],
  deletedSources: [],
  insufficientSources: [],
  degradedSources: [],
  wikiRepairPages: [{ title: "Türev grafiği", nextAction: "Citation ekle", curationStatus: "watch" }],
  sourceBackedConcepts: [{ conceptTitle: "Eğim", sourceTitle: "Türev notu", evidenceStatus: "ready" }],
};

const notebook = {
  title: "Notebook Studio",
  userSafeSummary: "Çalışma paketi, özet, sesli anlatım ve export burada bağlamsal araç olarak durur.",
  readinessStatus: "ready",
  packReadiness: "ready",
  recommendedPacks: [{ title: "Türev hızlı tekrar paketi", summary: "Ders notu, mini kontrol ve kaynak dayanakları.", packType: "review_pack", actions: [action("Paketi aç", "notebook")] }],
  artifactQueue: [{ title: "3 maddelik ders notu", previewOnly: true, renderFormat: "text", sourceBasis: "source_backed" }],
  exportPreviews: [{ previewType: "summary", exportLimitations: ["preview only"], readinessStatus: "preview_ready" }],
  sourceEvidenceLinks: [{ title: "Türev notu" }],
  wikiEvidenceLinks: [{ title: "Türev grafiği" }],
  conceptLinks: [{ title: "Eğim" }],
  examOutcomeLinks: [],
  studyRoomTraceLinks: [],
  tutorHandoffs: [action("Tutor'a sor", "tutor")],
  reviewHandoffs: [action("Mini quiz", "review")],
  sourceWikiHandoffs: [action("Kaynağı aç", "sources-wiki")],
  examWarRoomHandoffs: [],
  studyRoomHandoffs: [action("Ders odasına dön", "study-room")],
  missionControlWarnings: [],
  warnings: [],
};

const codeIde = {
  topicId: topic.id,
  readinessStatus: "limited",
  mode: "practice",
  activeLanguage: "csharp",
  activeTopic: "Döngüler",
  userSafeSummary: "Kod çalıştırma, hata kategorisi ve Tutor'a güvenli açıklama aktarımı.",
  runtimeReadiness: { status: "ready", decision: "editor_available", supportedLanguages: ["csharp"], warnings: [], reasonCodes: [] },
  lastAttemptSummary: { status: "none", safeTutorSummary: "Henüz kod denemesi yok." },
  repeatedErrorSummary: { repetitionCount: 0, repairSuggestion: "İlk denemeyi çalıştır." },
  recommendedActions: [action("Kod pratiğine başla", "code")],
  tutorHandoffs: [action("Hata açıklat", "tutor")],
  quizHandoffs: [],
  reviewHandoffs: [],
  wikiHandoffs: [],
  notebookHandoffs: [],
  missionControlWarnings: [],
  runtimeWarnings: [],
};

async function waitForServer(url, timeoutMs = 45000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const res = await fetch(url);
      if (res.ok || res.status < 500) return;
    } catch {}
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`Server did not start: ${url}`);
}

async function mockApi(page) {
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const p = url.pathname.replace(/^\/api/, "");
    const json = (body, status = 200) => route.fulfill({
      status,
      contentType: "application/json",
      body: JSON.stringify(body),
    });

    if (p === "/auth/refresh") return json({ token: "demo-token", user });
    if (p === "/user/me") return json(user);
    if (p === "/topics") return json([]);
    if (p === `/topics/${topic.id}`) return json(topic);
    if (p === `/topics/${topic.id}/sessions/latest`) return json({ id: "session-demo", topicId: topic.id });
    if (p === "/dashboard/today") return json({
      dailyFocusTitle: "Kısa konu onarımı ile başla",
      dailyFocusReason: "Kanıt az; küçük bir açıklama ve checkpoint daha güvenli.",
      missionControl: mission,
      studyCoach: { todayPlan: "Önce Tutor, sıkılırsan Study Room, sonra küçük quiz." },
      orkaLearningState: { status: "ready", activeTopicId: topic.id, conflicts: [] },
      weakConcepts: [{ label: "Grafikte eğim", userSafeStatus: "Mini tekrar iyi olur.", conceptKey: "slope" }],
      learningMemory: {
        strongTopics: [{ title: "Limit sezgisi", userSafeSummary: "Temel sezgi iyi." }],
        weakConcepts: [{ title: "Grafikte eğim", userSafeSummary: "Bir örnek daha iyi olur." }],
        weakTopics: [],
        recentMisconceptions: [],
      },
      sourceHealth: { status: "limited", userSafeLabel: "Kaynak dayanağı sınırlı", userSafeDetail: "Bir kaynak hazır, citation hâlâ izleniyor." },
      coordinationHealth: { metrics: [] },
    });
    if (p === "/dashboard/stats") return json({
      totalXP: 1240,
      currentStreak: 3,
      totalSections: 12,
      completedSections: 5,
      learningSignalBook: {
        weakSkills: [{ skillTag: "Grafik okuma", topicPath: "Türev / Grafik", wrongCount: 2, totalCount: 5, accuracy: 60 }],
        recentSignals: [{ label: "Mini kontrol", detail: "Bir kavram tekrar istiyor." }],
      },
    });
    if (p === "/quiz/stats") return json({
      correctAnswers: 8,
      totalQuizzes: 13,
      accuracy: 62,
      dailyProgress: [
        { date: "2026-06-01", total: 2, accuracy: 50 },
        { date: "2026-06-02", total: 3, accuracy: 66 },
        { date: "2026-06-03", total: 4, accuracy: 75 },
      ],
    });
    if (p === "/user/gamification") return json({
      level: 4,
      levelLabel: "Düzenli öğrenci",
      xpToNextLevel: 360,
      totalXP: 1240,
    });
    if (p === "/learning/mission-control") return json(mission);
    if (p === "/learning/study-coach") return json({ todayPlan: "Önce Tutor, sıkılırsan Study Room, sonra küçük quiz.", actions: [action("Study Room'a geç", "study-room")] });
    if (p === "/learning/orka-state") return json({ status: "ready", activeTopicId: topic.id, conflicts: [], memorySummary: "Demo öğrenme hafızası hazır." });
    if (p === "/classroom/study-room") return json(studyRoom);
    if (p === "/sources/wiki-pro") return json(sourceWiki);
    if (p === "/notebook-studio/pro") return json(notebook);
    if (p === "/code/learning-ide") return json(codeIde);
    if (p.includes("/war-room")) return json(examWarRoom);
    if (p === "/tools/capabilities") return json({ items: [] });
    if (p === "/chat/message" || p === "/chat/send") return json({ content: "Demo tutor cevabı: önce kavramı sadeleştirip sonra küçük bir kontrol yapalım.", message: "Demo tutor cevabı." });
    if (p === "/chat/stream") return json({ content: "" });
    if (p.startsWith("/wiki/")) return json({ pages: [], blocks: [], items: [] });
    if (p.startsWith("/sources")) return json([]);
    if (p.startsWith("/quiz") || p.startsWith("/review") || p.startsWith("/flashcards") || p.startsWith("/daily-challenge")) return json({ items: [], due: [], stats: {} });
    if (p.startsWith("/central-exams")) return json(examWarRoom);
    if (p.startsWith("/code")) return json(codeIde);
    return json({});
  });
}

async function initAuth(page, view = "home") {
  await page.addInitScript(({ topic, user, view }) => {
    localStorage.setItem("orka_token", "demo-token");
    localStorage.setItem("orka_user", JSON.stringify(user));
    localStorage.removeItem("orka_active_topic_id");
    if (!localStorage.getItem("orka_active_view")) {
      localStorage.setItem("orka_active_view", view);
    }
    localStorage.setItem(`orka_premium_tour_seen_v3_${user.id}`, "true");
  }, { topic, user, view });
}

async function capture(page, name) {
  await page.waitForLoadState("networkidle").catch(() => {});
  await page.waitForTimeout(800);
  const heading = await page.locator("h1, h2").first().textContent().catch(() => "");
  await page.screenshot({ path: path.join(outputDir, `${name}.png`), fullPage: true });
  return { file: `${name}.png`, heading: heading?.trim() ?? "" };
}

async function main() {
  await fs.rm(outputDir, { recursive: true, force: true });
  await fs.mkdir(outputDir, { recursive: true });

  const server = spawn("npm", ["run", "dev", "--", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], {
    cwd: root,
    shell: true,
    stdio: "ignore",
    windowsHide: true,
  });

  try {
    await waitForServer(baseURL);
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1440, height: 920 }, deviceScaleFactor: 1 });
    await mockApi(page);

    await page.goto(`${baseURL}/`);
    const manifest = [];
    manifest.push(await capture(page, "01-landing"));

    await page.goto(`${baseURL}/login`);
    manifest.push(await capture(page, "02-login"));

    await initAuth(page, "home");
    await page.goto(`${baseURL}/app`);
    manifest.push(await capture(page, "03-ana-kokpit"));

    const screens = [
      ["/app/tutor", "04-tutor"],
      ["/app/study-room", "05-study-room"],
      ["/app/review", "06-review-quiz"],
      ["/app/exams", "07-exam-war-room"],
      ["/app/sources", "08-sources-wiki"],
      ["/app/notebook", "09-notebook-studio"],
      ["/app/code", "10-code-ide"],
      ["/app/progress", "11-progress"],
      ["/app/settings", "12-settings-safety"],
    ];

    for (const [routePath, name] of screens) {
      await page.evaluate(() => {
        localStorage.removeItem("orka_active_topic_id");
      });
      await page.goto(`${baseURL}${routePath}`);
      manifest.push(await capture(page, name));
    }

    await fs.writeFile(path.join(outputDir, "manifest.json"), JSON.stringify(manifest, null, 2));
    await browser.close();
    console.log(outputDir);
  } finally {
    server.kill();
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
