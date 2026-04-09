import { test, expect } from '@playwright/test';

test.describe('Landing Sayfası', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
  });

  // ── Sayfa yükleme ──────────────────────────────────────
  test('logo ve başlık görünür', async ({ page }) => {
    await expect(page.locator('.landing-logo').first()).toBeVisible();
    await expect(page.locator('.landing-logo').first()).toHaveText('orka');
  });

  test('hero başlığı render edilir', async ({ page }) => {
    const title = page.locator('.hero-title');
    await expect(title).toBeVisible();
    await expect(title).toContainText('Öğrenmenin');
    await expect(title).toContainText('En Derin');
  });

  test('hero alt metni görünür', async ({ page }) => {
    await expect(page.locator('.hero-subtitle')).toBeVisible();
  });

  // ── Navigasyon ─────────────────────────────────────────
  test('nav linkleri görünür', async ({ page }) => {
    const nav = page.locator('.landing-nav-links');
    await expect(nav.getByRole('link', { name: 'Özellikler' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Nasıl Çalışır' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Fiyatlar' })).toBeVisible();
  });

  test('"Giriş Yap" linki login sayfasına yönlendirir', async ({ page }) => {
    await page.getByRole('link', { name: 'Giriş Yap' }).click();
    await expect(page).toHaveURL('/login');
  });

  test('"Ücretsiz Başla" nav butonu register sayfasına yönlendirir', async ({ page }) => {
    // Nav'daki kayıt butonu
    await page.locator('.landing-nav-cta a[href="/register"]').click();
    await expect(page).toHaveURL('/register');
  });

  // ── CTA butonları ──────────────────────────────────────
  test('"Hemen Başla" hero butonu register sayfasına yönlendirir', async ({ page }) => {
    await page.locator('.hero-cta a[href="/register"]').click();
    await expect(page).toHaveURL('/register');
  });

  // ── Bölümler ───────────────────────────────────────────
  test('6 özellik kartı render edilir', async ({ page }) => {
    const cards = page.locator('.feature-card');
    await expect(cards).toHaveCount(6);
  });

  test('5 öğrenme aşaması render edilir', async ({ page }) => {
    const phases = page.locator('.phase-item');
    await expect(phases).toHaveCount(5);
    await expect(phases.first()).toContainText('Keşif');
    await expect(phases.last()).toContainText('Tamamlandı');
  });

  test('fiyatlandırma bölümü görünür', async ({ page }) => {
    const cards = page.locator('.plan-card');
    await expect(cards).toHaveCount(2);
    await expect(cards.first()).toContainText('Ücretsiz');
    await expect(cards.last()).toContainText('Pro');
  });

  test('istatistik barı rakamları gösterir', async ({ page }) => {
    const stats = page.locator('.stat-number');
    await expect(stats.first()).toBeVisible();
  });

  test('footer logo görünür', async ({ page }) => {
    await expect(page.locator('.footer-logo')).toBeVisible();
  });

  // ── İstatistik bar ─────────────────────────────────────
  test('4 istatistik gösterilir', async ({ page }) => {
    await expect(page.locator('.stat-item')).toHaveCount(4);
  });
});
