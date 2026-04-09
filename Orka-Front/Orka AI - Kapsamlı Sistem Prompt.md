# Orka AI - Kapsamlı Sistem Prompt

## Proje Özeti

**Orka AI**, yapay zeka tarafından desteklenen, kişiselleştirilmiş bir öğrenme ekosistemidir. Kullanıcılar herhangi bir konuda Orka AI ile sohbet edebilir, yapılandırılmış kurslar alabilir, bilgi haritasında konuları keşfedebilir ve etkileşimli sınavlarla bilgilerini test edebilirler.

## Teknik Stack

- **Frontend**: React 19 + Vite + TypeScript
- **Styling**: Tailwind CSS v4 + Framer Motion
- **Routing**: Wouter (lightweight client-side router)
- **Markdown**: react-markdown + remark-gfm (tables, code blocks)
- **UI Components**: shadcn/ui (Button, Dialog, Card, etc.)
- **Icons**: Lucide React
- **State Management**: React Context (QuizHistoryContext) + useState

## Mimari Yapı

### Sayfa Yapısı

1. **Landing Page** (`/`) — Tanıtım sayfası, özellikler, video, CTA ("Giriş Yap")
2. **Login Page** (`/login`) — Giriş Yap / Üye Ol, OAuth seçenekleri
3. **Home/Chat Page** (`/app`) — Ana sohbet arayüzü, konuşma geçmişi, müfredatlar
4. **Courses Catalog** (`/courses`) — Kurs kataloğu, filtreleme, arama, ilerleme barları
5. **Course Detail** (`/courses/:id`) — Modül accordion, ders listesi, tamamlanan derslere tıklanınca wiki açılır
6. **Profile** (`/profile`) — İstatistikler, heatmap, başarımlar, hedefler
7. **Quiz History** (Home içinde panel) — Deneme geçmişi, doğruluk oranları

### Bileşen Hiyerarşisi

```
App.tsx (routing)
├── Landing.tsx
├── Login.tsx
├── Home.tsx (main app container)
│   ├── LeftSidebar.tsx
│   │   ├── Icon bar (Anasayfa, Kurslar, Wiki, Ayarlar)
│   │   ├── Son Sohbetler (conversation list)
│   │   └── Müfredatlar (collapsible topics tree)
│   ├── ChatPanel.tsx (center canvas)
│   │   ├── Welcome state
│   │   ├── ChatMessage[] (user + AI messages)
│   │   ├── QuizCard (when triggered)
│   │   └── Input field
│   ├── WikiDrawer.tsx (right panel, slide-in)
│   │   ├── Breadcrumbs
│   │   ├── Wiki content (markdown)
│   │   ├── Tables with borders
│   │   ├── Code blocks with Copy button
│   │   ├── Orka AI mini-chat bubble
│   │   └── Edit/Last updated info
│   ├── DashboardPanel.tsx (when "Anasayfa" selected)
│   ├── SettingsPanel.tsx (when "Ayarlar" selected)
│   └── QuizHistoryPanel.tsx (when "Geçmiş" selected)
├── Courses.tsx
├── CourseDetail.tsx
└── Profile.tsx
```

## Veri Modelleri

### Conversation
```typescript
interface Conversation {
  id: string;
  title: string;
  createdAt: Date;
  messages: ChatMessage[];
}

interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  type?: "text" | "quiz" | "thinking";
  timestamp: Date;
}
```

### Course
```typescript
interface Course {
  id: string;
  title: string;
  description: string;
  category: string;
  level: "Başlangıç" | "Orta" | "İleri";
  icon: string;
  totalModules: number;
  totalLessons: number;
  estimatedHours: number;
  progress: number;
  enrolled: boolean;
  tags: string[];
  instructor: string;
  rating: number;
  students: number;
  modules: CourseModule[];
}

interface CourseModule {
  id: string;
  title: string;
  description: string;
  duration: string;
  lessons: CourseLesson[];
}

interface CourseLesson {
  id: string;
  title: string;
  type: "video" | "article" | "quiz" | "exercise";
  duration: string;
  completed: boolean;
}
```

### Wiki Content
```typescript
interface WikiContent {
  topicId: string;
  subLessonId: string;
  title: string;
  content: string; // markdown
  keyPoints: string[];
  lastUpdated: string;
}
```

## Tasarım Prensipleri

### Renk Paleti (Zinc Monokrom Dark)
- **Background**: `zinc-950` (en koyu)
- **Surface**: `zinc-900`, `zinc-800` (kartlar, paneller)
- **Border**: `zinc-800/50`, `zinc-700/30` (subtle)
- **Text**: `zinc-100` (başlıklar), `zinc-400` (body), `zinc-600` (muted)
- **Accent**: `emerald-500` (başarı), `amber-400` (uyarı), `red-400` (hata)

### Tipografi
- **Display**: Inter 700 (başlıklar, h1-h2)
- **Body**: Inter 400-500 (paragraflar, UI text)
- **Mono**: Courier New (kod blokları)

### Spacing & Radius
- **Padding**: 4px, 8px, 12px, 16px, 24px, 32px
- **Border Radius**: 8px (kartlar), 12px (büyük bileşenler), 4px (butonlar)
- **Gap**: 8px, 12px, 16px, 20px

### Animasyonlar
- **Transition**: 150-200ms (hover, focus)
- **Drawer**: 300ms slide-in (Framer Motion)
- **Accordion**: 200ms expand/collapse
- **Fade**: 150ms opacity changes

## Önemli Akışlar

### 1. Chat Sohbeti
1. Kullanıcı `/app` sayfasına girer
2. Sidebar'da "Yeni sohbet" butonuna tıklar veya mesaj yazarak başlar
3. Yeni Conversation oluşturulur, sidebar'da görünür
4. AI yanıtı markdown ile render edilir (başlıklar, listeler, tablolar, kod)
5. Kod bloklarında "Kopyala" butonu mevcut
6. Random olarak quiz kartı tetiklenebilir (messageCount > 1)
7. Quiz kartında Previous/Next navigasyonu, Check Answer feedback

### 2. Kurs İşlemi
1. Sidebar'da "Kurslar" → `/courses` kataloğu açılır
2. Kurslar kategori, arama, level filtreleriyle gösterilir
3. Kurs kartına tıkla → `/courses/:id` detay sayfası
4. Modülleri açıp dersleri görebilir
5. **Tamamlanan derslere tıklanınca** → İlgili wiki içeriği sağ panelde açılır
6. Wiki panelinde breadcrumb, tablo, kod, Orka mini-chat

### 3. Wiki Keşfi
1. Sidebar "Wiki" → Müfredatlar ağacı görünür
2. Konuya tıkla → Wiki drawer açılır
3. Breadcrumb: Home / Wiki / AI Concepts / Neural Networks
4. Markdown content, Key Points, Edit button, Last updated
5. Sağ altta Orka mini-chat: wiki'ye sorular sorabilir

### 4. Profil & İstatistikler
1. Sidebar "Profil" → Kişisel istatistikler
2. Heatmap (aktivite), başarımlar, hedefler
3. Konu ilerleme barları

## Mock Data Yapısı

### Konuşmalar (Sidebar)
- "Python'da dekoratörler nasıl çalışır?"
- "Machine Learning nedir?"
- "Web Development başlamak için..."

### Müfredatlar (Sidebar)
```
🐍 Python Fundamentals
  ├─ Variables & Data Types
  ├─ Control Flow
  ├─ Functions
  └─ Object-Oriented Programming

🧠 Machine Learning
  ├─ Supervised Learning
  ├─ Neural Networks
  └─ Model Evaluation
```

### Kurslar (Catalog)
1. Python Temelleri (Başlangıç, 68% ilerleme)
2. Machine Learning 101 (Orta, 0% ilerleme)
3. Web Development Bootcamp (İleri, 0% ilerleme)
4. C# Generics (Orta, 0% ilerleme)
5. Data Science Fundamentals (Başlangıç, 0% ilerleme)
6. Advanced Python (İleri, 0% ilerleme)

### Wiki İçeriği
- `py-1`: Variables & Data Types
- `ml-1`: Supervised Learning
- `csg-1`: C# Generics Introduction

## Önemli Fonksiyonlar

### `generateAIResponse(userMessage, messageCount)`
- Yapılandırılmış yanıt oluşturur (başlık, açıklama, kod, tablo, özet)
- `messageCount > 1` ise %30 ihtimalle quiz kartı ekler
- Markdown format

### `ensureConversation()`
- Aktif conversation yoksa yeni oluşturur
- Sidebar'da görünür hale getirir

### `handleLessonClick(lesson)`
- Tamamlanan derslere tıklanınca wiki açar
- `lessonToWikiMap` kullanarak ders başlığını wiki ID'ye eşler

## Geliştirme Kuralları

### Kod Kalitesi
- TypeScript strict mode
- React best practices (useEffect, useMemo, useCallback)
- Prop drilling yerine Context
- Reusable components

### Performans
- Lazy loading (Suspense)
- Memoization (useMemo, React.memo)
- Virtual scrolling (uzun listeler için)

### UX
- Loading states (skeleton, spinner)
- Error boundaries
- Keyboard navigation (Enter, Escape, Tab)
- Accessible focus rings

### Styling
- Tailwind utilities (no custom CSS)
- Consistent spacing/colors
- Dark mode only (no light theme)
- Responsive (mobile-first)

## Sonraki Geliştirmeler

1. **Backend Integration** — `web-db-user` özelliği ile gerçek LLM, database, auth
2. **Real-time Collaboration** — Kullanıcılar arası sohbet, shared notebooks
3. **Advanced Analytics** — Learning paths, skill recommendations
4. **Gamification** — Badges, leaderboards, streaks
5. **Mobile App** — React Native port
6. **API Integration** — External LLM providers (OpenAI, Claude, etc.)

## Troubleshooting

### Chat mesajları kayboluyorsa
- `ensureConversation()` state senkronizasyonunu kontrol et
- `activeConversationId` güncellemesinin mesaj eklenmesinden önce olduğundan emin ol

### Wiki drawer açılmıyorsa
- `lessonToWikiMap` içinde ders başlığı var mı kontrol et
- `wikiContents` içinde wiki ID var mı kontrol et

### Markdown tablo render edilmiyorsa
- `remark-gfm` plugin'i yüklü mü kontrol et
- ChatMessage ve WikiDrawer'da plugin kullanılıyor mu kontrol et

---

**Son Güncelleme**: 10 Nisan 2026
**Versiyon**: 1.0
