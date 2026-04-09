/**
 * E2E: Small Talk Guard — Selamlama / Kısa Mesaj Yeni Konu Oluşturmamalı
 *
 * Akış:
 *  1. Login
 *  2. "merhaba", "merha nasılsın", "slm", "hey" gibi mesajlar gönder
 *  3. Her mesajdan sonra GET /api/Topics → topic sayısı değişmemeli
 *  4. AI samimi kısa bir karşılama döner, hata metni içermemeli
 *
 * Assertions:
 *   A. Topic listesi boş kalıyor (yeni konu yaratılmadı)
 *   B. AI bubble görünür, hata metni yok
 *   C. Gerçek bir konu mesajı gönderdikten sonra topic yaratılıyor
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

async function getTopicCount(token) {
  const ctx     = await apiRequest.newContext();
  const headers = { Authorization: `Bearer ${token}` };
  const list    = await (await ctx.get(`${API}/api/Topics`, { headers })).json();
  await ctx.dispose();
  return Array.isArray(list) ? list.length : 0;
}

// ─────────────────────────────────────────────────────────────────────────────

test.describe('Small Talk Guard — Yeni Konu Oluşturma Engeli', () => {
  test.setTimeout(120_000);

  let token;

  test.beforeEach(async () => {
    token = await getToken();
    await deleteAllTopics(token);
  });

  test('Selamlama mesajları topic yaratmamalı, gerçek konu mesajı yaratmalı', async ({ page }) => {

    // ── A. LOGIN ─────────────────────────────────────────────────────────────
    await page.goto('/login');
    await page.locator('input[type="email"]').fill(EMAIL);
    await page.locator('input[type="password"]').fill(PASSWORD);
    await page.getByRole('button', { name: 'Giriş Yap' }).click();
    await page.waitForURL('/app', { timeout: 20_000 });
    await expect(page.locator('.loading-screen')).not.toBeVisible({ timeout: 15_000 });

    const textarea = page.locator('.chat-panel textarea');
    await expect(textarea).toBeEnabled({ timeout: 10_000 });

    // ── B. SMALL TALK MESAJLARI ───────────────────────────────────────────────
    const smallTalkMessages = [
      'merhaba',
      'merha nasılsın',   // yazım hatalı + ek kelime
      'slm',
    ];

    let countBefore = await getTopicCount(token);
    expect(countBefore).toBe(0);

    for (const msg of smallTalkMessages) {
      const bubblesBefore = await page.locator('.ai-bubble').count();

      await expect(textarea).toBeEnabled({ timeout: 10_000 });
      await textarea.fill(msg);
      await page.locator('.send-btn').click();

      // AI yanıtı bekleniyor
      await expect(page.locator('.ai-bubble')).toHaveCount(bubblesBefore + 1, { timeout: 60_000 });

      const bubble = page.locator('.ai-bubble').last();
      const text   = (await bubble.textContent()) ?? '';

      // Hata metni olmamalı
      expect(text.toLowerCase()).not.toContain('hata oluştu');
      expect(text.toLowerCase()).not.toContain('zihnim biraz karıştı');
      expect(text.trim().length).toBeGreaterThan(5);

      console.log(`[TEST] "${msg}" → AI: "${text.slice(0, 80)}"`);
    }

    // ── ASSERTION A: Topic listesi hâlâ boş ──────────────────────────────────
    const countAfterSmallTalk = await getTopicCount(token);
    expect(countAfterSmallTalk, 'Small talk sonrası topic yaratılmamalı').toBe(0);
    console.log(`✓ ${smallTalkMessages.length} small talk mesajı sonrası topic sayısı: ${countAfterSmallTalk}`);

    // ── C. GERÇEK KONU MESAJI — topic yaratılmalı ────────────────────────────
    await expect(textarea).toBeEnabled({ timeout: 10_000 });
    await textarea.fill('Python öğrenmek istiyorum');
    await page.locator('.send-btn').click();

    await expect(page.locator('.ai-bubble').last()).toBeVisible({ timeout: 30_000 });

    const countAfterRealTopic = await getTopicCount(token);
    expect(countAfterRealTopic, 'Gerçek konu sonrası topic yaratılmalı').toBeGreaterThan(0);
    console.log(`✓ Gerçek konu mesajı sonrası topic sayısı: ${countAfterRealTopic}`);

    console.log('✓ Small Talk Guard testi başarılı!');
  });
});
