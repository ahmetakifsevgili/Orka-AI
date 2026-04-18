---
description: Orka frontend (React 19) için bileşen mimarisi, tasarım sistemi, state yönetimi, SSE parse
globs:
  - "Orka-Front/src/**/*.tsx"
  - "Orka-Front/src/**/*.ts"
  - "Orka-Front/src/**/*.css"
alwaysApply: false
---

# Frontend Kuralları — React 19 / Vite 6 / Tailwind v4

## Dizin Yapısı

```
Orka-Front/src/
  components/     → Yeniden kullanılabilir UI bileşenleri (sayfa-agnostik)
  pages/          → Route seviyesi bileşenler (Home, Landing, Login, Profile...)
  services/api.ts → TEK Axios instance + tüm API namespace'leri (AuthAPI, ChatAPI, WikiAPI...)
  lib/types.ts    → Tüm shared TypeScript tipleri
  lib/mockData.ts → Thinking states ve sabit UI metinleri
  lib/quizParser.ts → Quiz JSON parse yardımcısı
  contexts/       → React Context provider'ları (Theme, Language, FontSize, QuizHistory)
```

## Tasarım Sistemi — Kesinlikle Uyulacak Kurallar

İzin verilen tüm Tailwind renk sınıfları **yalnızca** bu üç aile içindendir:

| Niyet | Sınıf ailesi | Ne zaman |
|---|---|---|
| Notr / arka plan / metin | `zinc-*` | Her yerde (varsayılan) |
| Başarı / tamamlanma / online | `emerald-*` | Sadece pozitif durumlar |
| Uyarı / kritik / quiz / halüsinasyon | `amber-*` | Tüm uyarı + hata + kritik durumlar |

**Kesinlikle YASAK:** `red-*`, `blue-*`, `purple-*`, `orange-*`, `indigo-*`, `cyan-*`, `violet-*`, `pink-*`, `rose-*`, tüm `bg-gradient-*`, glassmorphism, neon, top-navbar.  (Kritik durumlar için `amber-300` — normal uyarı için `amber-400/500` kullan — tonu şiddetten arttırarak ayırt et.)

- **Dark mode:** Uygulama her zaman dark mode'dadır; light mode geçişi yoktur.
- **Emojiler:** Kullanıcı açıkça istemediği sürece dosyalara emoji eklenmez.
- **Admin-only UI:** `storage.getUser()?.isAdmin === true` dışındaki kullanıcılara LLMOps/System Health HUD gösterilmez.  Yalnızca admin sekmesi sarı rozetle vurgulanır.

## Routing

- Router: **`wouter`** — `react-router-dom` kullanılmaz.
- Rotalar `App.tsx` içindeki `<Switch>` bloğunda tanımlanır.
- Korumalı rotalar `ProtectedRoute` HOC'u ile sarılır (token kontrolü `localStorage`'dan senkron yapılır).
- Mevcut rotalar: `/` (Landing) · `/login` · `/app` (Home) · `/profile` · `/courses`

## State Yönetimi

- **Global state yoktur** — Redux, Zustand kullanılmaz.
- Sayfa seviyesi state: `useState` + `useCallback` + `useRef` (bkz. `Home.tsx`).
- Paylaşılan state: React Context (Theme, Language, FontSize, QuizHistory).
- **`Home.tsx` merkezi state hub'ıdır:** `activeTopic`, `sessionId`, `messages`, `activeView`, `wikiTopicId` buradan yönetilir ve child component'lere prop olarak iletilir.

## Bileşen Mimarisi

### Ana Bileşenler ve Sorumlulukları

| Bileşen | Sorumluluk |
|---|---|
| `Home.tsx` | Ana shell — state yönetimi, view routing, topic/session yönetimi |
| `LeftSidebar.tsx` | Topic listesi, phase badge'leri, yeni topic modal |
| `ChatPanel.tsx` | SSE stream yönetimi, mesaj listesi, input alanı |
| `ChatMessage.tsx` | Tekil mesaj render'ı (text · quiz · topic_complete tipleri) |
| `WikiMainPanel.tsx` | Wiki içerik görüntüleme + Copilot panel + polling |
| `DashboardPanel.tsx` | İstatistik ve genel bakış |
| `SettingsPanel.tsx` | Kullanıcı ayarları |

### Prop Akışı Kuralı

```
Home.tsx
  ├── LeftSidebar (topics, activeTopic, onTopicClick, onTopicCreated...)
  ├── ChatPanel   (activeTopic, sessionId, messages, onOpenWiki, onTopicsRefresh...)
  └── WikiMainPanel (topicId, onClose)
```

`onOpenWiki` her zaman `Home.tsx`'den aşağıya prop olarak taşınır — context ile değil.

## SSE Stream Tüketimi (`ChatPanel.tsx`)

```typescript
// Standart SSE parse döngüsü:
for (const line of chunk.split("\n")) {
  if (!line.startsWith("data: ")) continue;
  const data = line.substring(6).replace(/\r$/, "");
  
  // Özel sinyal tespiti (sırası önemlidir — önce özel sinyaller kontrol edilir):
  if (data.startsWith("[THINKING:"))  → thinking state güncelle, mesaja yazma
  if (data.includes("[TOPIC_COMPLETE:")) → /\[TOPIC_COMPLETE:([^\]]+)\]/i ile ID çıkar
  if (data.includes("[PLAN_READY]"))  → markeri temizle, toast göster
  if (data === "[DONE]")             → döngüyü bitir
  if (data.startsWith("[ERROR]:"))   → toast.error göster
}

// Finalizasyon öncelik sırası:
completedTopicId var → topic_complete mesajı ekle
quizData var        → message.type = "quiz" yap
diğer               → isStreaming = false
```

## Mesaj Tipi Sistemi

```typescript
type MessageType = "text" | "quiz" | "plan" | "topic_complete";

interface ChatMessage {
  id: string;
  role: "user" | "ai";
  type: MessageType;
  content: string;
  quiz?: QuizData;
  completedTopicId?: string; // yalnızca type === "topic_complete"
  timestamp: Date;
  isStreaming?: boolean;
}
```

`ChatMessage.tsx` render önceliği:
1. `type === "topic_complete"` → tamamlama kartı (emerald renk, "Wikime Git" butonu)
2. `quiz !== undefined` → `QuizCard` bileşeni
3. diğer → `ReactMarkdown` (prose-invert stili)

## API Katmanı (`services/api.ts`)

- **Tek Axios instance** (`api`) — interceptor'lar bu instance üzerinden çalışır.
- Token refresh: 401'de `isRefreshing` bayrağı + pending queue pattern.
- Streaming endpoint'ler `fetch()` ile doğrudan çağrılır (Axios SSE desteklemez):
  ```typescript
  ChatAPI.streamMessage(data) → fetch("/api/chat/stream", { method: "POST", ... })
  ```
- Wiki copilot endpoint'leri (`/wiki/{id}/chat`, `/wiki/{id}/research`) de `fetch()` ile çağrılır.
- localStorage key'leri: `orka_token` · `orka_refresh` · `orka_user`

## Wiki Loading State & Polling (`WikiMainPanel.tsx`)

Wiki henüz hazır değilse (boş `pages` array):
- `isPolling = true` → 3 saniyede bir `WikiAPI.getTopicPages(topicId)` çağrılır.
- Max 20 deneme (60 saniye) sonra polling durur.
- Polling sırasında `WikiGeneratingSkeleton` bileşeni gösterilir.
- Veri gelince skeleton kalkar, wiki içeriği render edilir.

## Animasyon Kuralları

- **Sayfa geçişleri ve modal'lar:** `framer-motion` (`AnimatePresence` + `motion.div`).
- **Mikro etkileşimler:** Yalnızca Tailwind CSS (`transition-*`, `animate-*`).
- **Typewriter efekti:** Yalnızca SSE streaming bitmişse ve quiz değilse aktiftir.
- Thinking animasyonu: `ThinkingIndicator` bileşeni — rotating states `mockData.ts`'de tanımlı.

## Tailwind v4 Notları

- Config dosyası yoktur — Tailwind `@tailwindcss/vite` plugin'i üzerinden çalışır.
- Typography plugin: `@tailwindcss/typography` — AI mesajları için `prose prose-invert` sınıfları.
- `sidebar-scrollbar` gibi custom sınıflar `index.css`'de `@layer utilities` içinde tanımlanır.

## Hooks Kuralları

- **Hooks koşullu return'den önce çağrılır** — React Rules of Hooks ihlali yapılmaz.
- `useEffect` bağımlılık array'i her zaman eksiksiz yazılır.
- `useCallback` ile sarılan fonksiyonlar prop olarak geçildiğinde gereksiz re-render önlenir.

## Adlandırma Kuralları

```
Bileşenler      → PascalCase (ChatPanel, WikiMainPanel)
Hook'lar        → camelCase, use prefix (useEffect, useCallback)
Handler'lar     → handle prefix (handleSend, handleTopicClick)
Callback prop'ları → on prefix (onOpenWiki, onTopicsRefresh)
API namespace'leri → PascalCase + API suffix (ChatAPI, WikiAPI, TopicsAPI)
```

## Hızlı Kontrol Listesi — Yeni Bileşen Eklerken

- [ ] Prop tipleri `lib/types.ts`'de mi tanımlı? (Değilse oraya ekle)
- [ ] `onOpenWiki` gerekliyse `Home.tsx`'den prop olarak geliyor mu?
- [ ] Hooks koşulsuz çağrılıyor mu?
- [ ] Renkler zinc/emerald/amber paletinden mi?
- [ ] `npx tsc --noEmit` temiz mi?
