/**
 * E2E: Deep Plan → Continuous Flow  (No-F5 Regression Guard)
 *
 * İzolasyon stratejisi:
 *   - beforeEach: API üzerinden TÜM konuları siler → selectedTopic=null garantisi
 *   - Sıfır konu ile başlayan app'te mesaj yeni konu yaratır → AwaitingChoice → Seçenek 1/2
 *   - "1" seçilince: Deep Plan + İlk Ders → wikiUpdated=true → sidebar refresh
 */

import { test, expect, request as apiRequest } from '@playwright/test';

const API      = 'http://localhost:5065';
const EMAIL    = 'test@orka.ai';
const PASSWORD = 'TestPass123!';

// ── Yardımcı: backend auth token ────────────────────────────────────────────
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

// ── Yardımcı: test kullanıcısının tüm konularını sil ─────────────────────────
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

test.describe('Continuous Flow — Deep Plan → İlk Ders', () => {
  test.setTimeout(180_000);

  test.beforeEach(async () => {
    const token = await getToken();
    await deleteAllTopics(token);
  });

  test('Deep Plan (1) seçince 4 alt başlık sidebar\'a gelir ve ilk ders açılır', async ({ page }) => {

    // ── A. LOGIN ─────────────────────────────────────────────────────────────
    await page.goto('/login');
    await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 });

    await page.locator('input[type="email"]').fill(EMAIL);
    await page.locator('input[type="password"]').fill(PASSWORD);
    await page.getByRole('button', { name: 'Giriş Yap' }).click();

    await page.waitForURL('/app', { timeout: 20_000 });
    await expect(page.locator('.loading-screen')).not.toBeVisible({ timeout: 20_000 });

    // Cleanup sonrası sidebar gerçekten boş olmalı
    await expect(page.locator('.sidebar-topics .topic-item')).toHaveCount(0, { timeout: 10_000 });

    // ── B. TETİKLEME ─────────────────────────────────────────────────────────
    const textarea = page.locator('.chat-panel textarea');
    await expect(textarea).toBeEnabled({ timeout: 10_000 });

    await textarea.fill('C# Generics çalışmak istiyorum');
    await page.locator('.send-btn').click();

    // selectedTopic=null → yeni konu + AwaitingChoice → AI "Seçenek 1/2" döner
    const lastBubble = page.locator('.ai-bubble').last();
    await expect(lastBubble).toContainText('Seçenek', { timeout: 60_000 });

    // ── C. AKIŞ SEÇİMİ ───────────────────────────────────────────────────────
    await expect(textarea).toBeEnabled({ timeout: 15_000 });
    await textarea.fill('1');
    await page.locator('.send-btn').click();

    // Deep Plan + İlk Ders → wikiUpdated=true → toast
    await expect(page.getByText('Bilgi haritası güncellendi')).toBeVisible({
      timeout: 120_000,
    });

    // ── ASSERTION 1: Sidebar — parent + 4 alt başlık = 5 konu ────────────────
    // setTopics(latestTopics) tetiklenir; 0 + 1 (parent) + 4 (sub) = 5
    await expect(page.locator('.sidebar-topics .topic-item')).toHaveCount(5, {
      timeout: 20_000,
    });

    // ── ASSERTION 2: İlk Ders chat panelinde görünür ─────────────────────────
    // wikiUpdated sonrası messages sıfırlanır; tek ai-bubble = deep plan + ilk ders
    const finalBubble = page.locator('.ai-bubble').last();
    await expect(finalBubble).toBeVisible({ timeout: 10_000 });
    await expect(finalBubble).not.toContainText('Zihnim biraz karıştı');
    await expect(finalBubble).not.toContainText('hata oluştu');

    // "İlk Konumuz:" AgentOrchestrator'ın combine response'unda bulunur
    await expect(finalBubble).toContainText('İlk Konumuz', { timeout: 10_000 });

    // Debug bilgisi
    const allItems = await page.locator('.sidebar-topics .topic-item .topic-name').allTextContents();
    console.log('✓ Sidebar konular:', allItems);
    const lessonSnippet = (await finalBubble.textContent())?.slice(0, 150);
    console.log('✓ İlk ders (ilk 150 kar):', lessonSnippet);
  });
});
