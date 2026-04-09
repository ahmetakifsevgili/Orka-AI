/**
 * E2E: Natural Chat → /plan → Deep Plan → İlk Ders (No-F5 Regression Guard)
 *
 * Yeni UX Akışı:
 *  1. İlk mesaj → Doğal TutorAgent yanıtı (Seçenek menüsü YOK)
 *  2. "/plan" komutu → "Plan yapalım mı?" onay sorusu
 *  3. "evet" → Deep Plan (dinamik başlık sayısı) + İlk Ders → wikiUpdated=true → toast + sidebar refresh
 *
 * Assertions:
 *  A. Sidebar'da parent + alt başlıklar (dinamik sayı, en az 3)
 *  B. Chat'te "İlk Konumuz" içeren AI yanıtı görünür
 */

import { test, expect, request as apiRequest } from '@playwright/test';

const API      = 'http://localhost:5065';
const EMAIL    = 'test@orka.ai';
const PASSWORD = 'TestPass123!';

async function getToken() {
  const ctx = await apiRequest.newContext();
  const res  = await ctx.post(`${API}/api/Auth/login`, {
    data: { email: EMAIL, password: PASSWORD },
  });
  if (!res.ok()) throw new Error(`Login failed: ${res.status()} ${await res.text()}`);
  const { token } = await res.json();
  await ctx.dispose();
  return token;
}

async function deleteAllTopics(token) {
  const ctx     = await apiRequest.newContext();
  const headers = { Authorization: `Bearer ${token}` };
  const listRes = await ctx.get(`${API}/api/Topics`, { headers });
  const topics  = await listRes.json();
  console.log(`[cleanup] ${topics.length} konu silinecek.`);
  for (const t of topics) {
    const del = await ctx.delete(`${API}/api/Topics/${t.id}`, { headers });
    console.log(`[cleanup] ${t.title} → ${del.status()}`);
  }
  await ctx.dispose();
}

// ─────────────────────────────────────────────────────────────────────────────

test.describe('Continuous Flow — Natural Chat → Deep Plan → İlk Ders', () => {
  test.setTimeout(300_000);

  test.beforeEach(async () => {
    const token = await getToken();
    await deleteAllTopics(token);
  });

  test('Doğal sohbet → /plan → evet → alt başlıklar sidebar\'a gelir ve ilk ders açılır', async ({ page }) => {

    // ── A. LOGIN ─────────────────────────────────────────────────────────────
    await page.goto('/login');
    await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 });
    await page.locator('input[type="email"]').fill(EMAIL);
    await page.locator('input[type="password"]').fill(PASSWORD);
    await page.getByRole('button', { name: 'Giriş Yap' }).click();
    await page.waitForURL('/app', { timeout: 20_000 });
    await expect(page.locator('.loading-screen')).not.toBeVisible({ timeout: 20_000 });

    const textarea = page.locator('.chat-panel textarea');

    // ── B. İLK MESAJ: Doğal sohbet (Seçenek menüsü artık YOK) ───────────────
    await expect(textarea).toBeEnabled({ timeout: 10_000 });
    await textarea.fill('C# Generics çalışmak istiyorum');
    await page.locator('.send-btn').click();

    // Natural TutorAgent yanıtı bekleniyor — plan ipucu içermeli
    const firstBubble = page.locator('.ai-bubble').last();
    await expect(firstBubble).toBeVisible({ timeout: 60_000 });
    // Artık "Seçenek 1/2" menüsü değil, doğal yanıt geliyor
    await expect(firstBubble).not.toContainText('hata oluştu');

    // ── C. /PLAN KOMUTU ───────────────────────────────────────────────────────
    await expect(textarea).toBeEnabled({ timeout: 10_000 });
    await textarea.fill('/plan');
    await page.locator('.send-btn').click();

    // "Plan yapalım mı?" onay sorusu bekleniyor
    const planOfferBubble = page.locator('.ai-bubble').last();
    await expect(planOfferBubble).toContainText('Plan', { timeout: 30_000 });

    // ── D. ONAYLA ─────────────────────────────────────────────────────────────
    await expect(textarea).toBeEnabled({ timeout: 10_000 });
    await textarea.fill('evet');
    await page.locator('.send-btn').click();

    // Deep Plan + İlk Ders → wikiUpdated=true → toast
    await expect(page.getByText('Bilgi haritası güncellendi')).toBeVisible({ timeout: 240_000 });

    // ── ASSERTION A: Sidebar — parent + alt başlıklar (dinamik, en az 3) ─────
    await expect(page.locator('.sidebar-topics .topic-item').first()).toBeVisible({ timeout: 20_000 });
    const sidebarCount = await page.locator('.sidebar-topics .topic-item').count();
    console.log(`✓ Sidebar toplam item sayısı: ${sidebarCount}`);
    expect(sidebarCount).toBeGreaterThanOrEqual(3); // parent + en az 2 alt başlık

    // ── ASSERTION B: İlk Ders chat panelinde görünür ─────────────────────────
    const finalBubble = page.locator('.ai-bubble').last();
    await expect(finalBubble).toBeVisible({ timeout: 10_000 });
    await expect(finalBubble).not.toContainText('hata oluştu');
    await expect(finalBubble).toContainText('İlk Konumuz', { timeout: 10_000 });

    const allItems = await page.locator('.sidebar-topics .topic-item .topic-name').allTextContents();
    console.log('✓ Sidebar konular:', allItems);
    const lessonSnippet = (await finalBubble.textContent())?.slice(0, 150);
    console.log('✓ İlk ders (ilk 150 kar):', lessonSnippet);
  });
});
