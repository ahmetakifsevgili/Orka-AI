import { test, expect } from '@playwright/test';
import { mockBackend } from './helpers.js';

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LOGIN
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Giriş Sayfası', () => {
  test.beforeEach(async ({ page }) => {
    await mockBackend(page);
    await page.goto('/login');
  });

  test('marka paneli ve form görünür', async ({ page }) => {
    await expect(page.locator('.auth-brand')).toBeVisible();
    await expect(page.locator('.auth-form-title')).toHaveText('Giriş Yap');
    await expect(page.locator('input[type="email"]')).toBeVisible();
    await expect(page.locator('input[type="password"]')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Giriş Yap' })).toBeVisible();
  });

  test('auth brand logosu landing\'e yönlendirir', async ({ page }) => {
    await page.locator('.auth-brand-logo').click();
    await expect(page).toHaveURL('/');
  });

  test('"Ücretsiz Başla" linki register sayfasına yönlendirir', async ({ page }) => {
    await page.getByRole('link', { name: 'Ücretsiz Başla' }).click();
    await expect(page).toHaveURL('/register');
  });

  test('boş form gönderilemez (HTML5 validasyon)', async ({ page }) => {
    await page.getByRole('button', { name: 'Giriş Yap' }).click();
    // Tarayıcı HTML5 validasyonu devreye girer, URL değişmez
    await expect(page).toHaveURL('/login');
  });

  test('yanlış kimlik bilgileriyle hata mesajı gösterilir', async ({ page }) => {
    await page.locator('input[type="email"]').fill('yanlis@orka.dev');
    await page.locator('input[type="password"]').fill('YanlisParola');
    await page.getByRole('button', { name: 'Giriş Yap' }).click();

    const error = page.locator('.auth-error');
    await expect(error).toBeVisible();
    await expect(error).toContainText('E-posta veya şifre hatalı');
  });

  test('doğru kimlik bilgileriyle /app sayfasına yönlendirilir', async ({ page }) => {
    await page.locator('input[type="email"]').fill('test@orka.dev');
    await page.locator('input[type="password"]').fill('Test1234');
    await page.getByRole('button', { name: 'Giriş Yap' }).click();

    await expect(page).toHaveURL('/app');
  });

  test('giriş yapılırken buton devre dışı kalır', async ({ page }) => {
    await page.locator('input[type="email"]').fill('test@orka.dev');
    await page.locator('input[type="password"]').fill('Test1234');

    // Slow down the response to catch loading state
    await page.route('**/api/Auth/login', async (route) => {
      await new Promise(r => setTimeout(r, 400));
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: 't', refreshToken: 'r', user: { id: '1', email: 'test@orka.dev' } }),
      });
    });

    const btn = page.getByRole('button', { name: /Giriş/ });
    await btn.click();
    await expect(btn).toBeDisabled();
  });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// REGISTER
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Kayıt Sayfası', () => {
  test.beforeEach(async ({ page }) => {
    await mockBackend(page);
    await page.goto('/register');
  });

  test('form alanları görünür', async ({ page }) => {
    await expect(page.locator('.auth-form-title')).toHaveText('Hesap Oluştur');
    await expect(page.locator('input[placeholder="Ali"]')).toBeVisible();
    await expect(page.locator('input[placeholder="Yılmaz"]')).toBeVisible();
    await expect(page.locator('input[type="email"]')).toBeVisible();
    await expect(page.locator('input[type="password"]')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Ücretsiz Başla' })).toBeVisible();
  });

  test('"Giriş Yap" linki login sayfasına yönlendirir', async ({ page }) => {
    await page.getByRole('link', { name: 'Giriş Yap' }).click();
    await expect(page).toHaveURL('/login');
  });

  test('mevcut e-posta ile kayıt olunmaya çalışıldığında hata gösterilir', async ({ page }) => {
    await page.locator('input[placeholder="Ali"]').fill('Test');
    await page.locator('input[placeholder="Yılmaz"]').fill('Kullanıcı');
    await page.locator('input[type="email"]').fill('mevcut@orka.dev');
    await page.locator('input[type="password"]').fill('Parola1234');
    await page.getByRole('button', { name: 'Ücretsiz Başla' }).click();

    const error = page.locator('.auth-error');
    await expect(error).toBeVisible();
    await expect(error).toContainText('zaten kayıtlı');
  });

  test('geçerli bilgilerle kayıt olunduğunda /app sayfasına yönlendirilir', async ({ page }) => {
    await page.locator('input[placeholder="Ali"]').fill('Yeni');
    await page.locator('input[placeholder="Yılmaz"]').fill('Kullanıcı');
    await page.locator('input[type="email"]').fill('yeni@orka.dev');
    await page.locator('input[type="password"]').fill('Parola1234');
    await page.getByRole('button', { name: 'Ücretsiz Başla' }).click();

    await expect(page).toHaveURL('/app');
  });

  test('şifre minimum 8 karakter zorunluluğu', async ({ page }) => {
    await page.locator('input[placeholder="Ali"]').fill('Test');
    await page.locator('input[placeholder="Yılmaz"]').fill('User');
    await page.locator('input[type="email"]').fill('test2@orka.dev');
    await page.locator('input[type="password"]').fill('kisa');

    const btn = page.getByRole('button', { name: 'Ücretsiz Başla' });
    await btn.click();
    // HTML5 minlength devreye girer
    await expect(page).toHaveURL('/register');
  });

  test('kayıt sırasında buton yükleme metnini gösterir', async ({ page }) => {
    await page.route('**/api/Auth/register', async (route) => {
      await new Promise(r => setTimeout(r, 500));
      route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({ token: 't', refreshToken: 'r', user: { id: '1', email: 'x@y.com' } }),
      });
    });

    await page.locator('input[placeholder="Ali"]').fill('A');
    await page.locator('input[placeholder="Yılmaz"]').fill('B');
    await page.locator('input[type="email"]').fill('x@y.com');
    await page.locator('input[type="password"]').fill('Parola1234');

    await page.getByRole('button', { name: 'Ücretsiz Başla' }).click();
    await expect(page.getByRole('button', { name: 'Hesap oluşturuluyor...' })).toBeVisible();
  });

  // ── Korumalı rota ──────────────────────────────────────
  test('token olmadan /app açılırsa /login\'e yönlendirilir', async ({ page }) => {
    await page.goto('/app');
    await expect(page).toHaveURL('/login');
  });
});
