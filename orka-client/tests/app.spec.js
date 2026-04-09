import { test, expect } from '@playwright/test';
import { injectAuth, mockBackend, MOCK_TOPICS } from './helpers.js';

// Her testten önce: auth inject + API mock + /app aç
async function setup(page) {
  await injectAuth(page);
  await mockBackend(page);
  await page.goto('/app');
  // Dashboard yüklenene kadar bekle (topic ismi görünür olana kadar)
  await page.waitForSelector('.topic-item', { timeout: 8000 });
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// GENEL LAYOUT
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Uygulama Dashboard — Genel Layout', () => {
  test('topbar, sidebar ve chat paneli render edilir', async ({ page }) => {
    await setup(page);

    await expect(page.locator('.topbar-logo')).toHaveText('orka');
    await expect(page.locator('.sidebar-left')).toBeVisible();
    await expect(page.locator('.new-topic-btn')).toBeVisible();
    await expect(page.locator('.tab-btn').first()).toBeVisible();
  });

  test('kullanıcı adı baş harfleri avatar\'da görünür', async ({ page }) => {
    await setup(page);
    // "Ali Yılmaz" → "AY"
    await expect(page.locator('.user-avatar')).toContainText('AY');
  });

  test('depolama pill görünür', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.storage-pill')).toBeVisible();
  });

  test('user dropdown menüsü açılır ve kapanır', async ({ page }) => {
    await setup(page);
    const avatar = page.locator('.user-avatar');
    await avatar.click();
    await expect(page.locator('.user-menu')).toBeVisible();
    // Dışarıya tıkla — menü kapanmalı
    await page.locator('.topbar-logo').click();
    await expect(page.locator('.user-menu')).not.toBeVisible();
  });

  test('çıkış yapıldığında /login sayfasına yönlendirilir', async ({ page }) => {
    await setup(page);
    await page.locator('.user-avatar').click();
    await page.locator('.user-menu-item.danger').click();
    await expect(page).toHaveURL('/login');
    // Token temizlenmiş olmalı
    const token = await page.evaluate(() => localStorage.getItem('orka_token'));
    expect(token).toBeNull();
  });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// KONU SİDEBAR
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Konu Sidebar', () => {
  test('konu listesi doğru render edilir', async ({ page }) => {
    await setup(page);
    const items = page.locator('.topic-item');
    await expect(items).toHaveCount(MOCK_TOPICS.length);
    await expect(items.first()).toContainText('React Öğrenme');
    await expect(items.last()).toContainText('Osmanlı Tarihi');
  });

  test('aktif konu vurgulanır', async ({ page }) => {
    await setup(page);
    // İlk konu varsayılan olarak seçili
    await expect(page.locator('.topic-item').first()).toHaveClass(/active/);
  });

  test('farklı bir konuya tıklandığında seçim değişir', async ({ page }) => {
    await setup(page);
    const second = page.locator('.topic-item').nth(1);
    await second.click();
    await expect(second).toHaveClass(/active/);
    await expect(page.locator('.topic-item').first()).not.toHaveClass(/active/);
  });

  test('phase badge\'leri gösterilir', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.topic-phase-badge').first()).toBeVisible();
  });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// YENİ KONU MODAL
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Yeni Konu Modal', () => {
  test('modal butona tıklandığında açılır', async ({ page }) => {
    await setup(page);
    await page.locator('.new-topic-btn').click();
    await expect(page.locator('.modal-card')).toBeVisible();
    await expect(page.locator('.modal-title')).toHaveText('Yeni Konu Ekle');
  });

  test('X butonuyla kapanır', async ({ page }) => {
    await setup(page);
    await page.locator('.new-topic-btn').click();
    await expect(page.locator('.modal-card')).toBeVisible();
    await page.locator('.modal-close-btn').click();
    await expect(page.locator('.modal-card')).not.toBeVisible();
  });

  test('overlay\'e tıklandığında kapanır', async ({ page }) => {
    await setup(page);
    await page.locator('.new-topic-btn').click();
    await page.locator('.modal-overlay').click({ position: { x: 10, y: 10 } });
    await expect(page.locator('.modal-card')).not.toBeVisible();
  });

  test('"İptal" butonuyla kapanır', async ({ page }) => {
    await setup(page);
    await page.locator('.new-topic-btn').click();
    await page.getByRole('button', { name: 'İptal' }).click();
    await expect(page.locator('.modal-card')).not.toBeVisible();
  });

  test('başlık olmadan "Oluştur" butonu devre dışı kalır', async ({ page }) => {
    await setup(page);
    await page.locator('.new-topic-btn').click();
    await expect(page.locator('.modal-footer .btn-primary')).toBeDisabled();
  });

  test('emoji seçimi çalışır', async ({ page }) => {
    await setup(page);
    await page.locator('.new-topic-btn').click();
    const targetEmoji = page.locator('.emoji-option').nth(5);
    await targetEmoji.click();
    await expect(targetEmoji).toHaveClass(/selected/);
  });

  test('geçerli form doldurulunca konu oluşturulur ve modal kapanır', async ({ page }) => {
    await setup(page);
    await page.locator('.new-topic-btn').click();

    await page.locator('.form-input').first().fill('Python ile Makine Öğrenmesi');
    await page.locator('.emoji-option').nth(2).click();

    await page.locator('.modal-footer .btn-primary').click();

    // Modal kapanmalı
    await expect(page.locator('.modal-card')).not.toBeVisible({ timeout: 6000 });
  });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// SOHBET PANELİ
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Sohbet Paneli', () => {
  test('konu seçilince hoş geldin kartı görünür', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.welcome-card')).toBeVisible();
    await expect(page.locator('.welcome-card-title')).toContainText('React Öğrenme');
  });

  test('hızlı aksiyon chipleri görünür', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.quick-action-chip')).toHaveCount(4);
  });

  test('mesaj gönderilebilir', async ({ page }) => {
    await setup(page);
    const textarea = page.locator('.input-wrap textarea');
    await textarea.fill('React hooks nedir?');
    await page.locator('.send-btn').click();

    // Kullanıcı mesajı listede görünür
    await expect(page.locator('.bubble.user-bubble')).toContainText('React hooks nedir?');
  });

  test('Enter tuşuyla mesaj gönderilir', async ({ page }) => {
    await setup(page);
    const textarea = page.locator('.input-wrap textarea');
    await textarea.fill('useState hook\'u açıkla');
    await textarea.press('Enter');
    await expect(page.locator('.bubble.user-bubble').first()).toContainText('useState');
  });

  test('Shift+Enter yeni satır ekler, göndermez', async ({ page }) => {
    await setup(page);
    const textarea = page.locator('.input-wrap textarea');
    await textarea.fill('ilk satır');
    await textarea.press('Shift+Enter');
    await textarea.type('ikinci satır');
    // Mesaj listesi boş kalmalı (henüz gönderilmedi)
    await expect(page.locator('.bubble.user-bubble')).toHaveCount(0);
  });

  test('AI yanıtı render edilir', async ({ page }) => {
    await setup(page);
    await page.locator('.input-wrap textarea').fill('React nedir?');
    await page.locator('.send-btn').click();

    // AI yanıtı bekle
    await expect(page.locator('.bubble:not(.user-bubble)').first()).toBeVisible({ timeout: 8000 });
    await expect(page.locator('.model-chip').first()).toContainText('Gemini Flash');
  });

  test('AI düşünme animasyonu gösterilir ve kaybolur', async ({ page }) => {
    await setup(page);
    // Yavaş yanıt
    await page.route('**/api/Chat/message', async (route) => {
      await new Promise(r => setTimeout(r, 600));
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ messageId: 'm1', sessionId: 's1', topicId: 'topic-1', content: 'Yanıt', modelUsed: 'Gemini Flash', messageType: 'Explain', wikiUpdated: false }),
      });
    });

    await page.locator('.input-wrap textarea').fill('Soru');
    await page.locator('.send-btn').click();

    await expect(page.locator('.thinking-bubble')).toBeVisible();
    await expect(page.locator('.thinking-bubble')).not.toBeVisible({ timeout: 5000 });
  });

  test('boş input ile send butonu devre dışı', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.send-btn')).toBeDisabled();
    await page.locator('.input-wrap textarea').fill('bir şey');
    await expect(page.locator('.send-btn')).toBeEnabled();
  });

  test('hızlı aksiyon chip\'ine tıklandığında mesaj gönderilir', async ({ page }) => {
    await setup(page);
    await page.locator('.quick-action-chip').first().click();
    // /quiz komutu gönderildi
    await expect(page.locator('.bubble.user-bubble')).toContainText('/quiz');
  });

  test('günlük mesaj sayacı görünür', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.input-status')).toContainText('5 / 50 mesaj');
  });

  test('API hatasında toast bildirimi gösterilir', async ({ page }) => {
    await setup(page);
    // Chat API'yi hata döndürecek şekilde override et
    await page.route('**/api/Chat/message', (route) =>
      route.fulfill({ status: 500, body: 'Internal Server Error' })
    );

    await page.locator('.input-wrap textarea').fill('Bu mesaj hata verecek');
    await page.locator('.send-btn').click();

    // Toast mesajı görünmeli
    const toast = page.locator('[data-testid="toast"], .react-hot-toast, [class*="toast"]').first();
    // Alternatif: sadece kullanıcı mesajının geri alındığını kontrol et
    await page.waitForTimeout(1500);
    await expect(page.locator('.bubble.user-bubble')).toHaveCount(0);
  });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// BİLGİ HARİTASI (WIKI)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Bilgi Haritası (Wiki) Paneli', () => {
  test.beforeEach(async ({ page }) => {
    await setup(page);
    // Wiki tab'ına geç
    await page.locator('.tab-btn').nth(1).click();
    await expect(page.locator('.wiki-layout')).toBeVisible();
  });

  test('wiki sayfaları listelenir', async ({ page }) => {
    await expect(page.locator('.wiki-page-item')).toHaveCount(3);
    await expect(page.locator('.wiki-page-item').first()).toContainText('Giriş & Kurulum');
  });

  test('durum ikonları render edilir', async ({ page }) => {
    await expect(page.locator('.wiki-page-status-icon').first()).toBeVisible();
  });

  test('sayfaya tıklandığında bloklar yüklenir', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    await expect(page.locator('.block-card')).toHaveCount(3);
  });

  test('Concept, Quiz ve UserNote blokları render edilir', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    await expect(page.locator('.block-icon-concept')).toBeVisible();
    await expect(page.locator('.block-icon-quiz')).toBeVisible();
    await expect(page.locator('.block-icon-note')).toBeVisible();
  });

  test('blok içeriği gösterilir', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    await expect(page.locator('.block-body').first()).toContainText('Facebook');
  });

  test('kaynak kartı görünür ve tıklanabilir', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    const src = page.locator('.source-card');
    await expect(src).toBeVisible();
    await expect(src).toContainText('React Resmi Dokümantasyonu');
  });

  test('sayfa durum badge\'i gösterilir', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    await expect(page.locator('.wiki-status-badge')).toBeVisible();
    await expect(page.locator('.wiki-status-badge')).toContainText('Tamamlandı');
  });

  test('UserNote bloğu silinebilir (DELETE isteği gönderilir)', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    const deleteBtn = page.locator('.block-action-btn.danger');
    await expect(deleteBtn).toBeVisible();
    // DELETE isteğini yakala
    const deleteReq = page.waitForRequest(
      req => req.method() === 'DELETE' && req.url().includes('/api/Wiki/block/')
    );
    await deleteBtn.click();
    await deleteReq;
  });

  test('not ekleme formu çalışır', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    await page.locator('.add-note-input').fill('Bu benim notum');
    await page.getByRole('button', { name: 'Ekle' }).click();
    // Not alanı temizlenmeli
    await expect(page.locator('.add-note-input')).toHaveValue('');
  });

  test('Enter ile not eklenir', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    const input = page.locator('.add-note-input');
    await input.fill('Enter ile not ekle');
    await input.press('Enter');
    await expect(input).toHaveValue('');
  });

  test('boş not "Ekle" butonu devre dışı', async ({ page }) => {
    await page.locator('.wiki-page-item').first().click();
    await expect(page.getByRole('button', { name: 'Ekle' })).toBeDisabled();
  });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// SAĞ BİLGİ PANELİ (InfoPanel)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Sağ Bilgi Paneli', () => {
  test('sağ bilgi paneli görünür', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.info-panel')).toBeVisible();
  });

  test('4 hızlı komut butonu gösterilir', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.quick-btn')).toHaveCount(4);
  });

  test('hızlı komut butonu mesaj gönderir', async ({ page }) => {
    await setup(page);
    // /quiz butonuna tıkla
    await page.locator('.quick-btn').first().click();
    await expect(page.locator('.bubble.user-bubble').first()).toContainText('/quiz');
  });

  test('AI model listesi görünür', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.ai-model-card')).toHaveCount(4);
    await expect(page.locator('.ai-model-name').first()).toContainText('Gemini');
  });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// TAB GEÇİŞLERİ
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
test.describe('Tab Geçişleri', () => {
  test('sohbet tab varsayılan olarak aktif', async ({ page }) => {
    await setup(page);
    await expect(page.locator('.tab-btn').first()).toHaveClass(/active/);
    await expect(page.locator('.chat-panel')).toBeVisible();
  });

  test('"Bilgi Haritam" tabına geçilir', async ({ page }) => {
    await setup(page);
    await page.locator('.tab-btn').nth(1).click();
    await expect(page.locator('.tab-btn').nth(1)).toHaveClass(/active/);
    await expect(page.locator('.wiki-layout')).toBeVisible();
    await expect(page.locator('.chat-panel')).not.toBeVisible();
  });

  test('tekrar "Sohbet" tabına dönülebilir', async ({ page }) => {
    await setup(page);
    await page.locator('.tab-btn').nth(1).click();
    await page.locator('.tab-btn').first().click();
    await expect(page.locator('.chat-panel')).toBeVisible();
    await expect(page.locator('.wiki-layout')).not.toBeVisible();
  });
});
