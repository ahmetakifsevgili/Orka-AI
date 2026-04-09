/** Shared test data & API mock helpers */

export const MOCK_USER = {
  id: 'user-1',
  email: 'test@orka.dev',
  firstName: 'Ali',
  lastName: 'Yılmaz',
  plan: 'Free',
  dailyMessageCount: 5,
  dailyLimit: 50,
  storageUsedMB: 12.4,
  storageLimitMB: 3072,
};

export const MOCK_TOPICS = [
  {
    id: 'topic-1',
    title: 'React Öğrenme',
    emoji: '⚛️',
    category: 'Bilgisayar Bilimi',
    currentPhase: 'ActiveStudy',
    totalSections: 5,
    completedSections: 2,
  },
  {
    id: 'topic-2',
    title: 'Osmanlı Tarihi',
    emoji: '🏛️',
    category: 'Tarih',
    currentPhase: 'Discovery',
    totalSections: 0,
    completedSections: 0,
  },
];

export const MOCK_TOPIC_DETAIL = {
  topic: {
    id: 'topic-1',
    title: 'React Öğrenme',
    emoji: '⚛️',
    category: 'Bilgisayar Bilimi',
    currentPhase: 'ActiveStudy',
    totalSections: 5,
    completedSections: 2,
    lastStudySnapshot: 'Hooks konusundan devam ediyoruz.',
  },
  sessions: [],
  wikiPages: [],
};

export const MOCK_WIKI_PAGES = [
  { pageId: 'page-1', title: 'Giriş & Kurulum', status: 'completed', blockCount: 4 },
  { pageId: 'page-2', title: 'Bileşenler',       status: 'learning',  blockCount: 2 },
  { pageId: 'page-3', title: 'Hooks',             status: 'pending',   blockCount: 0 },
];

export const MOCK_PAGE_CONTENT = {
  page: { id: 'page-1', title: 'Giriş & Kurulum', status: 'completed' },
  blocks: [
    {
      id: 'block-1',
      blockType: 'Concept',
      title: 'React Nedir?',
      content: 'React, Facebook tarafından geliştirilen açık kaynaklı bir JavaScript kütüphanesidir.',
      source: 'Gemini',
    },
    {
      id: 'block-2',
      blockType: 'Quiz',
      title: 'Pekiştirme Soruları',
      content: 'Q: Virtual DOM nedir ve ne işe yarar?\nA: Virtual DOM, gerçek DOM\'un hafıza içi temsilidir.',
      source: 'Mistral',
    },
    {
      id: 'block-3',
      blockType: 'UserNote',
      title: 'Kişisel Notum',
      content: 'Bileşenler props alır ve state tutar.',
      source: 'user',
    },
  ],
  sources: [
    { id: 'src-1', title: 'React Resmi Dokümantasyonu', url: 'https://react.dev', type: 'website' },
  ],
};

export const MOCK_CHAT_RESPONSE = {
  messageId: 'msg-100',
  sessionId: 'session-1',
  topicId: 'topic-1',
  content: 'React bir UI kütüphanesidir. Bileşen tabanlı bir yapı sunar ve tek yönlü veri akışı kullanır.',
  modelUsed: 'Gemini Flash',
  messageType: 'Explain',
  wikiUpdated: true,
  wikiPageId: 'page-1',
  isNewTopic: false,
};

/**
 * Inject a fake JWT token into localStorage before page loads
 * (must be called before page.goto)
 */
export async function injectAuth(page) {
  await page.addInitScript((user) => {
    localStorage.setItem('orka_token', 'fake-test-jwt-token');
    localStorage.setItem('orka_refresh', 'fake-refresh-token');
    localStorage.setItem('orka_user', JSON.stringify(user));
  }, MOCK_USER);
}

/**
 * Mock all backend API routes with fixture data
 */
export async function mockBackend(page) {
  // User
  await page.route('**/api/User/me', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(MOCK_USER) })
  );

  // Topics list
  await page.route(/\/api\/Topics$/, (route) => {
    if (route.request().method() === 'GET') {
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(MOCK_TOPICS) });
    } else if (route.request().method() === 'POST') {
      const body = JSON.parse(route.request().postData() || '{}');
      route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'topic-new',
          title: body.title,
          emoji: body.emoji || '📚',
          category: body.category || 'Genel',
          currentPhase: 'Discovery',
          totalSections: 0,
          completedSections: 0,
        }),
      });
    } else {
      route.continue();
    }
  });

  // Single topic — returns correct topic based on URL id
  await page.route(/\/api\/Topics\/topic-[\w-]+$/, (route) => {
    const url = route.request().url();
    const id = url.split('/').pop();
    const meta = MOCK_TOPICS.find(t => t.id === id);
    const topicData = meta
      ? { ...MOCK_TOPIC_DETAIL.topic, id: meta.id, title: meta.title, emoji: meta.emoji, category: meta.category, currentPhase: meta.currentPhase, totalSections: meta.totalSections, completedSections: meta.completedSections }
      : { ...MOCK_TOPIC_DETAIL.topic, id, title: 'Yeni Konu', currentPhase: 'Discovery', totalSections: 0, completedSections: 0 };
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ topic: topicData, sessions: [], wikiPages: [] }),
    });
  });

  // Chat
  await page.route('**/api/Chat/message', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(MOCK_CHAT_RESPONSE) })
  );

  // Wiki pages list
  await page.route(/\/api\/Wiki\/topic-\w+$/, (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(MOCK_WIKI_PAGES) })
  );

  // Wiki page content
  await page.route(/\/api\/Wiki\/page\/page-\w+$/, (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(MOCK_PAGE_CONTENT) })
  );

  // Add note
  await page.route(/\/api\/Wiki\/page\/page-\w+\/note$/, (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ blockId: 'block-new', message: 'Note added' }) })
  );

  // Delete block
  await page.route(/\/api\/Wiki\/block\/\w+$/, (route) => {
    if (route.request().method() === 'DELETE') {
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
    } else {
      route.continue();
    }
  });

  // Auth endpoints (login / register)
  await page.route('**/api/Auth/login', (route) => {
    const body = JSON.parse(route.request().postData() || '{}');
    if (body.email === 'test@orka.dev' && body.password === 'Test1234') {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: 'new-token', refreshToken: 'new-refresh', user: MOCK_USER }),
      });
    } else {
      route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'E-posta veya şifre hatalı.' }),
      });
    }
  });

  await page.route('**/api/Auth/register', (route) => {
    const body = JSON.parse(route.request().postData() || '{}');
    if (body.email === 'mevcut@orka.dev') {
      route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Bu e-posta adresi zaten kayıtlı.' }),
      });
    } else {
      route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({ token: 'new-token', refreshToken: 'new-refresh', user: { ...MOCK_USER, email: body.email } }),
      });
    }
  });
}
