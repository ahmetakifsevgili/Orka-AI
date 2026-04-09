# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: small-talk-guard.spec.js >> Small Talk Guard — Yeni Konu Oluşturma Engeli >> Selamlama mesajları topic yaratmamalı, gerçek konu mesajı yaratmalı
- Location: tests\small-talk-guard.spec.js:58:3

# Error details

```
Error: Small talk sonrası topic yaratılmamalı

expect(received).toBe(expected) // Object.is equality

Expected: 0
Received: 1
```

# Page snapshot

```yaml
- generic [ref=e3]:
  - generic [ref=e4]:
    - link "orka" [ref=e5] [cursor=pointer]:
      - /url: /
    - generic [ref=e6]:
      - img [ref=e7]
      - generic [ref=e10]: Konu ara veya başlat...
      - generic [ref=e11]: ⌘K
    - generic [ref=e12]:
      - generic [ref=e14]: 0 MB
      - button "Pro'ya Geç" [ref=e16] [cursor=pointer]:
        - img [ref=e17]
        - text: Pro'ya Geç
      - generic [ref=e19] [cursor=pointer]: U
  - generic [ref=e20]:
    - generic [ref=e21]:
      - button "Yeni Konu" [ref=e23] [cursor=pointer]:
        - img [ref=e24]
        - text: Yeni Konu
      - generic [ref=e25]: Konularım
      - generic [ref=e27]:
        - text: Henüz konu yok.
        - text: Yeni bir konu ekleyin.
    - generic [ref=e30]:
      - generic [ref=e31]:
        - generic [ref=e32]:
          - generic [ref=e33]:
            - generic [ref=e34]: U
            - generic [ref=e35]:
              - generic [ref=e37]: Sen
              - generic [ref=e38]: merhaba
          - generic [ref=e39]:
            - generic [ref=e40]: ◎
            - generic [ref=e41]:
              - generic [ref=e42]:
                - generic [ref=e43]: Tutor-Agent
                - generic [ref=e44]: Yanıt
              - generic [ref=e45]:
                - paragraph [ref=e46]: Merhaba! Seninle sohbet etmeye başlamak için heyecanlıyım. Konuşmak istediğin bir konu var mı, yoksa ben sana bazı önerilerde bulunabilir miyim? Örneğin, bir sorun hakkında konuşabilir, bir konuyu öğrenmek isteyebilir veya sadece sohbet edebiliriz. Sana nasıl yardım edebilirim?
                - separator [ref=e47]
                - paragraph [ref=e48]:
                  - emphasis [ref=e49]:
                    - text: İstersen
                    - code [ref=e50]: /plan
                    - text: yazarak bu konu için yapılandırılmış bir müfredat planı da oluşturabilirim.
          - generic [ref=e51]:
            - generic [ref=e52]: U
            - generic [ref=e53]:
              - generic [ref=e55]: Sen
              - generic [ref=e56]: merha nasılsın
          - generic [ref=e57]:
            - generic [ref=e58]: ◎
            - generic [ref=e59]:
              - generic [ref=e60]:
                - generic [ref=e61]: Tutor-Agent
                - generic [ref=e62]: Yanıt
              - generic [ref=e63]:
                - paragraph [ref=e64]: Merhaba, nasılsın? Güzel bir gün geçirmiş olmalısın! Sohbet için çok teşekkür ederim.
                - paragraph [ref=e65]: Bu konuya geçmeden önce, seninle konuşacağımız konuyu belirlemek için bir önerim var. Benimle konuşmak istediğin bir konu var mı? Bir sorununla başa çıkarken yardıma ihtiyacın var mı? Yoksa bir konuyu öğrenmek istiyorsun ve bana akıl hocası olmasını istiyorsun?
                - paragraph [ref=e66]: Belirlediği bir konuya bağlı kalmadan sadece sohbet de edebiliriz, tabii ki! Sen ne ister misin?
                - paragraph [ref=e67]: (Yanıt olarak, senin ne istediğini bilmek için sormaktan mutluluk duyacağım)
          - generic [ref=e68]:
            - generic [ref=e69]: U
            - generic [ref=e70]:
              - generic [ref=e72]: Sen
              - generic [ref=e73]: slm
          - generic [ref=e74]:
            - generic [ref=e75]: ◎
            - generic [ref=e76]:
              - generic [ref=e77]:
                - generic [ref=e78]: Tutor-Agent
                - generic [ref=e79]: Yanıt
              - generic [ref=e80]:
                - paragraph [ref=e81]: "Merhaba! Seni selamlıyorum. Sohbeti hızlandırmak için sorularını cevaplayıp, konuyu belirlemeye çalışıyorum. Yine de aynı soruları sormak istiyorum:"
                - paragraph [ref=e82]: Bir sorununla başa çıkarken yardıma ihtiyacın var mı? Yoksa bir konuyu öğrenmek istiyorsun ve bana akıl hocası olmasını istiyorsun?
                - paragraph [ref=e83]: Belirlediğimiz bir konuya bağlı kalmadan sadece sohbet edebiliriz, tabii ki! Sen ne ister misin?
        - generic [ref=e84]:
          - generic [ref=e85]:
            - textbox "Öğrenmek istediğiniz yeni bir konu yazarak başlayın..." [ref=e86]
            - generic [ref=e87]:
              - button [ref=e88] [cursor=pointer]:
                - img [ref=e89]
              - button [ref=e91] [cursor=pointer]:
                - img [ref=e92]
              - button [disabled] [ref=e95]:
                - img [ref=e96]
          - generic [ref=e98]:
            - generic [ref=e99]:
              - generic [ref=e102]: Gemini Flash
              - generic [ref=e103]: ·
              - generic [ref=e104]: 0 / 50 mesaj
            - generic [ref=e105]: Enter gönder · Shift+Enter satır
      - generic [ref=e106]:
        - generic [ref=e107]: Bu Konuda
        - generic [ref=e108]:
          - generic [ref=e109]: Hızlı Komutlar
          - button "/quiz — Test et" [disabled] [ref=e110] [cursor=pointer]:
            - img [ref=e111]
            - text: /quiz — Test et
          - button "/interview — Mülakat" [disabled] [ref=e114] [cursor=pointer]:
            - img [ref=e115]
            - text: /interview — Mülakat
          - button "/summary — Özet al" [disabled] [ref=e120] [cursor=pointer]:
            - img [ref=e121]
            - text: /summary — Özet al
          - button "/plan — Plan oluştur" [disabled] [ref=e123] [cursor=pointer]:
            - img [ref=e124]
            - text: /plan — Plan oluştur
          - generic [ref=e125]: Aktif AI Modeller
          - generic [ref=e128]:
            - generic [ref=e129]: Gemini Flash
            - generic [ref=e130]: Açıklama · Plan · Özet
          - generic [ref=e133]:
            - generic [ref=e134]: Groq LLaMA
            - generic [ref=e135]: Yönlendirme · Değerlendirme
          - generic [ref=e138]:
            - generic [ref=e139]: OpenRouter
            - generic [ref=e140]: Mülakat · Quiz · Araştırma
          - generic [ref=e143]:
            - generic [ref=e144]: Mistral
            - generic [ref=e145]: Wiki küratörü
```

# Test source

```ts
  4   |  * Akış:
  5   |  *  1. Login
  6   |  *  2. "merhaba", "merha nasılsın", "slm", "hey" gibi mesajlar gönder
  7   |  *  3. Her mesajdan sonra GET /api/Topics → topic sayısı değişmemeli
  8   |  *  4. AI samimi kısa bir karşılama döner, hata metni içermemeli
  9   |  *
  10  |  * Assertions:
  11  |  *   A. Topic listesi boş kalıyor (yeni konu yaratılmadı)
  12  |  *   B. AI bubble görünür, hata metni yok
  13  |  *   C. Gerçek bir konu mesajı gönderdikten sonra topic yaratılıyor
  14  |  */
  15  | 
  16  | import { test, expect, request as apiRequest } from '@playwright/test';
  17  | 
  18  | const API      = 'http://localhost:5065';
  19  | const EMAIL    = 'test@orka.ai';
  20  | const PASSWORD = 'TestPass123!';
  21  | 
  22  | async function getToken() {
  23  |   const ctx = await apiRequest.newContext();
  24  |   const res  = await ctx.post(`${API}/api/Auth/login`, { data: { email: EMAIL, password: PASSWORD } });
  25  |   const { token } = await res.json();
  26  |   await ctx.dispose();
  27  |   return token;
  28  | }
  29  | 
  30  | async function deleteAllTopics(token) {
  31  |   const ctx     = await apiRequest.newContext();
  32  |   const headers = { Authorization: `Bearer ${token}` };
  33  |   const list    = await (await ctx.get(`${API}/api/Topics`, { headers })).json();
  34  |   for (const t of list) await ctx.delete(`${API}/api/Topics/${t.id}`, { headers });
  35  |   await ctx.dispose();
  36  | }
  37  | 
  38  | async function getTopicCount(token) {
  39  |   const ctx     = await apiRequest.newContext();
  40  |   const headers = { Authorization: `Bearer ${token}` };
  41  |   const list    = await (await ctx.get(`${API}/api/Topics`, { headers })).json();
  42  |   await ctx.dispose();
  43  |   return Array.isArray(list) ? list.length : 0;
  44  | }
  45  | 
  46  | // ─────────────────────────────────────────────────────────────────────────────
  47  | 
  48  | test.describe('Small Talk Guard — Yeni Konu Oluşturma Engeli', () => {
  49  |   test.setTimeout(120_000);
  50  | 
  51  |   let token;
  52  | 
  53  |   test.beforeEach(async () => {
  54  |     token = await getToken();
  55  |     await deleteAllTopics(token);
  56  |   });
  57  | 
  58  |   test('Selamlama mesajları topic yaratmamalı, gerçek konu mesajı yaratmalı', async ({ page }) => {
  59  | 
  60  |     // ── A. LOGIN ─────────────────────────────────────────────────────────────
  61  |     await page.goto('/login');
  62  |     await page.locator('input[type="email"]').fill(EMAIL);
  63  |     await page.locator('input[type="password"]').fill(PASSWORD);
  64  |     await page.getByRole('button', { name: 'Giriş Yap' }).click();
  65  |     await page.waitForURL('/app', { timeout: 20_000 });
  66  |     await expect(page.locator('.loading-screen')).not.toBeVisible({ timeout: 15_000 });
  67  | 
  68  |     const textarea = page.locator('.chat-panel textarea');
  69  |     await expect(textarea).toBeEnabled({ timeout: 10_000 });
  70  | 
  71  |     // ── B. SMALL TALK MESAJLARI ───────────────────────────────────────────────
  72  |     const smallTalkMessages = [
  73  |       'merhaba',
  74  |       'merha nasılsın',   // yazım hatalı + ek kelime
  75  |       'slm',
  76  |     ];
  77  | 
  78  |     let countBefore = await getTopicCount(token);
  79  |     expect(countBefore).toBe(0);
  80  | 
  81  |     for (const msg of smallTalkMessages) {
  82  |       const bubblesBefore = await page.locator('.ai-bubble').count();
  83  | 
  84  |       await expect(textarea).toBeEnabled({ timeout: 10_000 });
  85  |       await textarea.fill(msg);
  86  |       await page.locator('.send-btn').click();
  87  | 
  88  |       // AI yanıtı bekleniyor
  89  |       await expect(page.locator('.ai-bubble')).toHaveCount(bubblesBefore + 1, { timeout: 60_000 });
  90  | 
  91  |       const bubble = page.locator('.ai-bubble').last();
  92  |       const text   = (await bubble.textContent()) ?? '';
  93  | 
  94  |       // Hata metni olmamalı
  95  |       expect(text.toLowerCase()).not.toContain('hata oluştu');
  96  |       expect(text.toLowerCase()).not.toContain('zihnim biraz karıştı');
  97  |       expect(text.trim().length).toBeGreaterThan(5);
  98  | 
  99  |       console.log(`[TEST] "${msg}" → AI: "${text.slice(0, 80)}"`);
  100 |     }
  101 | 
  102 |     // ── ASSERTION A: Topic listesi hâlâ boş ──────────────────────────────────
  103 |     const countAfterSmallTalk = await getTopicCount(token);
> 104 |     expect(countAfterSmallTalk, 'Small talk sonrası topic yaratılmamalı').toBe(0);
      |                                                                           ^ Error: Small talk sonrası topic yaratılmamalı
  105 |     console.log(`✓ ${smallTalkMessages.length} small talk mesajı sonrası topic sayısı: ${countAfterSmallTalk}`);
  106 | 
  107 |     // ── C. GERÇEK KONU MESAJI — topic yaratılmalı ────────────────────────────
  108 |     await expect(textarea).toBeEnabled({ timeout: 10_000 });
  109 |     await textarea.fill('Python öğrenmek istiyorum');
  110 |     await page.locator('.send-btn').click();
  111 | 
  112 |     await expect(page.locator('.ai-bubble').last()).toBeVisible({ timeout: 30_000 });
  113 | 
  114 |     const countAfterRealTopic = await getTopicCount(token);
  115 |     expect(countAfterRealTopic, 'Gerçek konu sonrası topic yaratılmalı').toBeGreaterThan(0);
  116 |     console.log(`✓ Gerçek konu mesajı sonrası topic sayısı: ${countAfterRealTopic}`);
  117 | 
  118 |     console.log('✓ Small Talk Guard testi başarılı!');
  119 |   });
  120 | });
  121 | 
```