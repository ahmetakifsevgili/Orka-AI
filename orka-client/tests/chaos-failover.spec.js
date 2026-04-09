/**
 * E2E: Chaos Monkey — Groq Down → Failover (Zero-Downtime Proof)
 *
 * Akış (Yeni UX):
 *  1. Login + "C# nedir?" → Doğal TutorAgent yanıtı (Seçenek menüsü YOK)
 *  2. "Detay ver" → Learning state → GERÇEK AI çağrısı
 *     Playwright bu isteğe X-Chaos-Fail: Groq enjekte eder.
 *     Backend: GroqService exception fırlatır → AIServiceChain fallback chain devreye girer.
 *
 * Assertions:
 *   A. HTTP 200 (not 500)
 *   B. Yeni AI bubble görünür, hata metni yok, gerçek içerik var (> 50 kar)
 */

import { test, expect, request as apiRequest } from '@playwright/test';

const API      = 'http://localhost:5065';
const EMAIL    = 'test@orka.ai';
const PASSWORD = 'TestPass123!';

async function getToken() {
  const ctx = await apiRequest.newContext();
  const res  = await ctx.post(`${API}/api/Auth/login`, { data: { email: EMAIL, password: PASSWORD } });
  const { token } = await res.json();
  await ctx.dispose();
  return token;
}

async function deleteAllTopics(token) {
  const ctx     = await apiRequest.newContext();
  const headers = { Authorization: `Bearer ${token}` };
  const list    = await (await ctx.get(`${API}/api/Topics`, { headers })).json();
  for (const t of list) await ctx.delete(`${API}/api/Topics/${t.id}`, { headers });
  await ctx.dispose();
}

// ─────────────────────────────────────────────────────────────────────────────

test.describe('Chaos Monkey — Groq Failover', () => {
  test.setTimeout(180_000);

  test.beforeEach(async () => {
    const token = await getToken();
    await deleteAllTopics(token);
  });

  test('X-Chaos-Fail: Groq → 200 döner, AI yanıtı görünür, hata yok', async ({ page }) => {

    // ── A. LOGIN ─────────────────────────────────────────────────────────────
    await page.goto('/login');
    await page.locator('input[type="email"]').fill(EMAIL);
    await page.locator('input[type="password"]').fill(PASSWORD);
    await page.getByRole('button', { name: 'Giriş Yap' }).click();
    await page.waitForURL('/app', { timeout: 20_000 });
    await expect(page.locator('.loading-screen')).not.toBeVisible({ timeout: 15_000 });

    const textarea = page.locator('.chat-panel textarea');
    await expect(textarea).toBeEnabled({ timeout: 10_000 });

    // ── B. MESAJ 1: Yeni konu aç → doğal TutorAgent yanıtı ──────────────────
    await textarea.fill('C# nedir?');
    await page.locator('.send-btn').click();

    // Doğal yanıt bekleniyor — hata içermemeli
    const firstBubble = page.locator('.ai-bubble').last();
    await expect(firstBubble).toBeVisible({ timeout: 30_000 });
    await expect(firstBubble).not.toContainText('hata oluştu');
    const firstText = (await firstBubble.textContent()) ?? '';
    expect(firstText.trim().length).toBeGreaterThan(20);
    console.log(`[TEST] İlk yanıt (ilk 80 kar): "${firstText.slice(0, 80)}"`);

    // ── C. CHAOS ROUTE: Bir sonraki /api/Chat/message isteğine header enjekte ─
    let chaosActivated = false;
    await page.route('**/api/Chat/message', async (route) => {
      if (!chaosActivated) {
        chaosActivated = true;
        console.log('[TEST] 🐒 Chaos header enjekte ediliyor: X-Chaos-Fail: Groq');
        await route.continue({
          headers: {
            ...route.request().headers(),
            'X-Chaos-Fail': 'Groq',
          },
        });
      } else {
        await route.continue();
      }
    });

    // ── D. MESAJ 2: GERÇEK AI çağrısı — Groq kaos altında, fallback devreye ──
    const bubblesBefore = await page.locator('.ai-bubble').count();

    const [chatResponse] = await Promise.all([
      page.waitForResponse(
        r => r.url().includes('/api/Chat/message'),
        { timeout: 120_000 }
      ),
      (async () => {
        await expect(textarea).toBeEnabled({ timeout: 15_000 });
        await textarea.fill('Detay ver');
        await page.locator('.send-btn').click();
      })(),
    ]);

    const capturedStatus = chatResponse.status();
    console.log(`[TEST] Backend HTTP yanıtı: ${capturedStatus}`);

    // Yeni AI bubble oluşmasını bekle
    await expect(page.locator('.ai-bubble')).toHaveCount(bubblesBefore + 1, { timeout: 15_000 });
    const newBubble = page.locator('.ai-bubble').last();
    await expect(newBubble).toBeVisible();

    // ── ASSERTION 1: HTTP 200 ─────────────────────────────────────────────────
    expect(capturedStatus).toBe(200);
    console.log(`✓ HTTP Status: ${capturedStatus} — Groq down, failover başarılı!`);

    // ── ASSERTION 2: Hata metni yok ──────────────────────────────────────────
    const bubbleText = (await newBubble.textContent()) ?? '';
    const errorTerms = ['zihnim biraz karıştı', 'hata oluştu', 'başarısız oldu', 'api hatası'];
    for (const term of errorTerms) {
      expect(bubbleText.toLowerCase(), `Hata terimi "${term}" bulunmamalı`).not.toContain(term);
    }

    // ── ASSERTION 3: Gerçek içerik var ───────────────────────────────────────
    expect(bubbleText.trim().length).toBeGreaterThan(50);

    console.log(`✓ Fallback AI Yanıtı (ilk 200 kar): ${bubbleText.slice(0, 200)}`);
    console.log('✓ Chaos Monkey testi başarılı — Zero-Downtime kanıtlandı!');
  });
});
