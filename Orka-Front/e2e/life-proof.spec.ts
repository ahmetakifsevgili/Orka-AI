import { expect, test } from "@playwright/test";

const apiUrl = (process.env.ORKA_API_URL ?? "http://localhost:5065").replace(/\/$/, "");

test.describe("Authenticated Orka browser life proof", () => {
  test("opens the logged-in app shell and verifies core learning surfaces", async ({ page, request }) => {
    const timestamp = new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 17);
    const randomSuffix = Math.random().toString(36).slice(2, 8);
    const runId = `${timestamp}-${randomSuffix}`;
    const email = `browser-life-${runId}@orka.local`;
    const password = `BrowserLife${runId}!`;

    const register = await request.post(`${apiUrl}/api/auth/register`, {
      data: {
        firstName: "Browser",
        lastName: "Life",
        email,
        password,
      },
    });
    expect(register.ok(), await register.text()).toBeTruthy();

    const login = await request.post(`${apiUrl}/api/auth/login`, {
      data: { email, password },
    });
    expect(login.ok(), await login.text()).toBeTruthy();
    const auth = await login.json();
    expect(auth.token).toBeTruthy();
    expect(auth.user?.id).toBeTruthy();

    const topic = await request.post(`${apiUrl}/api/topics`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: {
        title: `Browser Life Proof ${runId}`,
        emoji: "B",
        category: "Lifetest",
      },
    });
    expect(topic.ok(), await topic.text()).toBeTruthy();
    const topicBody = await topic.json();
    expect(topicBody.id).toBeTruthy();

    const consoleErrors: string[] = [];
    const failedRequests: string[] = [];
    page.on("console", (message) => {
      if (message.type() === "error") consoleErrors.push(message.text());
    });
    page.on("requestfailed", (request) => {
      const url = request.url();
      if (!url.includes("/api/auth/refresh")) {
        failedRequests.push(`${request.method()} ${url}: ${request.failure()?.errorText ?? "unknown"}`);
      }
    });

    await page.addInitScript(
      ({ token, user, topicId }) => {
        localStorage.setItem("orka_token", token);
        localStorage.setItem("orka_user", JSON.stringify(user));
        localStorage.setItem("orka_active_topic_id", topicId);
        localStorage.setItem("orka_active_view", "home");
        localStorage.setItem(`orka_premium_tour_seen_v3_${user.id}`, "true");
      },
      { token: auth.token, user: auth.user, topicId: topicBody.id },
    );

    await page.goto("/app");
    await expect(page).toHaveURL(/\/app/);
    await expect(page.getByText("Home / Mission Control")).toBeVisible({ timeout: 20000 });
    const introClose = page.getByRole("button", { name: "Close" });
    if (await introClose.isVisible().catch(() => false)) {
      await introClose.click();
    }

    const expectedSurfaces = [
      "Study Room",
      "Exam",
      "Sources",
      "Wiki",
      "Notebook Studio",
      "Quiz / Checkpoint",
      "Progress",
    ];

    for (const label of expectedSurfaces) {
      await expect(page.getByText(label).first()).toBeVisible({ timeout: 20000 });
    }

    await page.getByRole("button", { name: "practice" }).click();
    await expect(page.getByText("Adaptive Practice Workspace")).toBeVisible({ timeout: 20000 });
    await expect(page.getByText("IDE laboratuvar").first()).toBeVisible({ timeout: 20000 });

    const body = page.locator("body");
    await expect(body).toContainText("Orka AI");
    await expect(body).not.toContainText("rawPrompt");
    await expect(body).not.toContainText("answerKey");
    await expect(body).not.toContainText("correctAnswer");

    expect(consoleErrors, consoleErrors.join("\n")).toEqual([]);
    expect(failedRequests, failedRequests.join("\n")).toEqual([]);
  });
});
