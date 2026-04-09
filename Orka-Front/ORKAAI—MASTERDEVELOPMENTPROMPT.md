# ORKA AI — MASTER DEVELOPMENT PROMPT
**For a new AI agent to rebuild the project from scratch in one shot.**

---

## 1. PROJECT OVERVIEW

You are building **Orka AI** — an intelligent, agentic learning ecosystem. This is **not** a generic chatbot. It is a structured, curriculum-driven AI tutor that helps users master technical subjects through a combination of conversational AI, auto-generated learning curricula, interactive quizzes, and crystallized knowledge wikis.

The core metaphor is a **"Learning Command Center"**: the user has a persistent, evolving knowledge map on the left, a focused AI conversation in the center, and a living knowledge wiki that opens on the right when they click any topic. Every interaction is designed to make the user feel like they are in control of a powerful, intelligent system — not just chatting with a bot.

**Key differentiator from a chatbot:** Orka AI builds a persistent knowledge graph for the user. When the user asks to learn something, the AI generates a structured curriculum (a `Topic` with `SubLesson` nodes), adds it to the sidebar, and each sub-lesson has a corresponding wiki page that gets populated with crystallized notes.

---

## 2. TECH STACK

| Layer | Technology | Notes |
|---|---|---|
| Framework | React 19 + Vite | TypeScript strict mode |
| Styling | Tailwind CSS v4 | No external UI libraries for layout |
| Icons | `lucide-react` | Consistent icon set |
| Markdown | `react-markdown` | **Critical** — all AI responses must render as formatted markdown |
| Animation | `framer-motion` | Subtle, functional animations only |
| Notifications | `sonner` | Toast notifications |
| Routing | `wouter` | Lightweight client-side routing |

**Install command:**
```bash
pnpm add react-markdown framer-motion lucide-react sonner wouter
```

---

## 3. CORE DATA TYPES

Define these TypeScript interfaces in `src/lib/types.ts`. Every component must use these types — no `any`.

```typescript
export type MessageRole = "user" | "ai";
export type MessageType = "text" | "quiz" | "plan";

export interface QuizOption {
  id: string;
  text: string;
  isCorrect: boolean;
}

export interface QuizData {
  question: string;
  options: QuizOption[];
  explanation: string;
}

export interface ChatMessage {
  id: string;
  role: MessageRole;
  type: MessageType;   // "text" | "quiz" | "plan"
  content: string;     // Markdown string for AI, plain text for user
  quiz?: QuizData;     // Only present when type === "quiz"
  timestamp: Date;
  topicId?: string;
}

export interface SubLesson {
  id: string;
  title: string;
  completed: boolean;
}

export interface Topic {
  id: string;
  title: string;
  icon: string;        // Emoji icon
  subLessons: SubLesson[];
  createdAt: Date;
}

export interface WikiContent {
  topicId: string;
  subLessonId: string;
  title: string;
  content: string;     // Full Markdown content
  keyPoints: string[];
  lastUpdated: Date;
}

export interface WikiNote {
  id: string;
  subLessonId: string;
  content: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface QuizAttempt {
  id: string;
  messageId: string;
  question: string;
  selectedOptionId: string;
  isCorrect: boolean;
  explanation: string;
  timestamp: Date;
}
```

---

## 4. CORE BUSINESS LOGIC & WORKFLOWS

### 4A. The AI Response Engine (`src/lib/mockData.ts`)

The `generateAIResponse(userMessage: string, topics: Topic[]): AIResponse` function is the brain of the mock AI. It must implement these decision rules **in order**:

**Rule 1 — Small Talk Guard:**
If the message is a greeting (`"hello"`, `"hi"`, `"hey"`, `"merhaba"`, `"selam"`) or is fewer than 4 characters, return a friendly welcome message explaining what Orka AI is and how to use `/plan`. This prevents the AI from treating greetings as educational queries.

**Rule 2 — `/plan` Trigger (Deep Plan):**
If the message contains the string `/plan`, the AI must:
1. Parse the topic name from the message (e.g., `"I want to learn React Hooks /plan"` → topic = `"React Hooks"`).
2. Generate a `Topic` object with 4 `SubLesson` nodes (Foundations, Intermediate, Advanced, Real-World Applications).
3. Return a `type: "plan"` response with a rich Markdown curriculum overview as `content`.
4. Include the `newTopic: Topic` in the response so the caller can add it to the sidebar.

**Rule 3 — Quiz Trigger:**
Approximately every 2nd or 3rd substantive educational message (use `Math.random() > 0.55`), return a `type: "quiz"` response. Include a `QuizData` object from a pre-defined pool of 4–6 quiz questions covering Python, C#, algorithms, and ML concepts.

**Rule 4 — Default Educational Response:**
For all other messages, return a `type: "plan"` response (so it renders as markdown) with a substantive, well-formatted educational explanation. Use markdown headings (`##`, `###`), bold text, bullet lists, and code blocks. The response should feel like a knowledgeable tutor, not a search engine.

**The `AIResponse` interface:**
```typescript
export interface AIResponse {
  content: string;
  type: "text" | "quiz" | "plan";
  quiz?: QuizData;
  newTopic?: Topic;
}
```

### 4B. Auto-Wiki Sync

When a user clicks a `SubLesson` in the left sidebar, the app must:
1. Look up the corresponding `WikiContent` from `wikiContents` (a `Record<string, WikiContent>` in `mockData.ts`).
2. Open the right-side `WikiDrawer` and display the content.
3. Render the `WikiContent.content` field as full markdown using `react-markdown` with `prose prose-invert`.

Pre-populate `wikiContents` with at least 3 entries (e.g., `"py-1"` for Variables & Data Types, `"ml-1"` for Supervised Learning, `"csg-1"` for C# Generics Introduction) with rich, multi-section markdown content including code blocks and tables.

### 4C. Quiz Card Interaction

When a `ChatMessage` has `type === "quiz"`, render a `<QuizCard>` component inline in the chat. The quiz flow:
1. User sees the question and 4 options as a clean multiple-choice list.
2. User clicks an option → it gets selected (radio-style highlight).
3. User clicks "Check Answer" → the card reveals correct/incorrect feedback and the explanation text.
4. The attempt is saved to `QuizHistoryContext` (global state) for the Quiz History page.
5. A "Try Again" button resets the card.

### 4D. Streaming / Thinking States

When the user sends a message, simulate a multi-step thinking process before the AI responds:
1. Show a `ThinkingIndicator` component with a sequence of status messages:
   - `"🔍 Searching knowledge base..."`
   - `"📚 Planning curriculum..."`
   - `"🧠 Analyzing your learning style..."`
   - `"✍️ Generating response..."`
2. Each state lasts ~600ms.
3. After all states, display the AI message with a word-by-word streaming effect (or simply fade in).

---

## 5. LAYOUT ARCHITECTURE (100vh Flexbox — STRICT)

The root layout is a **full-screen, no-scroll container**. There is **absolutely no top navigation bar**. The entire UI lives in a single `h-screen flex overflow-hidden` div.

```
┌─────────────────────────────────────────────────────────────┐
│  LEFT SIDEBAR (w-64)  │  CENTER CANVAS (flex-1)  │  WIKI   │
│  bg-zinc-950          │  bg-zinc-900             │  DRAWER │
│                       │                          │  (slide)│
│  [OrcaLogo + Title]   │  [Scrollable Messages]   │         │
│  [New Topic Button]   │  [max-w-3xl mx-auto]     │         │
│                       │                          │         │
│  [Topic List]         │                          │         │
│  [SubLesson Items]    │                          │         │
│                       │                          │         │
│  [User Profile]       │  [Sticky Input Area]     │         │
└─────────────────────────────────────────────────────────────┘
```

### Pane A: Left Sidebar (`LeftSidebar.tsx`)

```
className="w-64 flex-shrink-0 bg-zinc-950 border-r border-zinc-800 flex flex-col h-full"
```

**Structure (top to bottom):**

1. **Header** (`px-4 py-5`): The `<OrcaLogo />` SVG (w-5 h-5) inline with the text `"Orka AI"` in `font-semibold text-zinc-100`. Below it, a ghost "New Topic" button (`text-xs text-zinc-500 hover:text-zinc-300`).

2. **Topic List** (`flex-1 overflow-y-auto px-2 py-2`): For each `Topic`, render a collapsible section:
   - Topic header: emoji icon + title in `text-sm font-medium text-zinc-300`. A chevron icon for expand/collapse.
   - When expanded, show `SubLesson` items indented with `pl-4`. Each sub-lesson is a button: `text-xs text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900/50 rounded px-2 py-1.5`. When active (currently open in wiki), highlight with `text-zinc-100 bg-zinc-800/50`.
   - Completed sub-lessons show a subtle checkmark icon.

3. **User Profile** (`mt-auto border-t border-zinc-800 px-4 py-4`): A small avatar circle (initials), username in `text-sm text-zinc-300`, and a link to the Profile page.

### Pane B: Center Canvas (`ChatPanel.tsx`)

```
className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden"
```

**Structure:**

1. **Message Scroll Area** (`flex-1 overflow-y-auto`):
   - Inner wrapper: `max-w-3xl mx-auto w-full px-6 py-8`
   - **Welcome state** (when `messages.length === 0`): A centered welcome with `"What do you want to learn today?"` heading and 4 quick-prompt suggestion chips.

2. **Message Rendering Rules (CRITICAL):**
   - **User messages:** Right-aligned. Bubble with `bg-zinc-800/50 rounded-2xl rounded-tr-sm px-4 py-3 max-w-md text-sm text-zinc-100`.
   - **AI messages:** Left-aligned. **NO background bubble.** Just an 8×8 avatar (`bg-zinc-800 rounded-lg text-xs font-semibold`) + the content rendered directly on the `bg-zinc-900` canvas.
   - **AI text/plan messages:** Render `message.content` through `react-markdown` with `className="prose prose-invert prose-sm max-w-none"`. This is **non-negotiable** — raw markdown strings must never be displayed.
   - **AI quiz messages:** Render the text content first, then the `<QuizCard>` component below it.

3. **Sticky Input Area** (`flex-shrink-0 border-t border-zinc-800 bg-zinc-900 px-6 py-4`):
   - Inner wrapper: `max-w-3xl mx-auto w-full`
   - Input box: `flex items-end gap-3 px-4 py-3 bg-zinc-800/80 rounded-xl border border-zinc-700 hover:border-zinc-600 transition-colors`
   - Auto-resizing `<textarea>` with `bg-transparent resize-none outline-none text-sm text-zinc-100 placeholder-zinc-500`
   - Send button: minimal, icon-only (`<Send className="w-4 h-4" />`), `text-zinc-500 hover:text-zinc-300`
   - Hint text below: `text-xs text-zinc-600` — "Press Enter to send · Shift+Enter for new line"

### Pane C: Wiki Drawer (`WikiDrawer.tsx`)

The wiki drawer slides in from the right when a sub-lesson is clicked. It is **not** a modal — it is a persistent side panel that coexists with the chat.

```
className="w-80 flex-shrink-0 bg-zinc-950 border-l border-zinc-800 flex flex-col h-full"
```

**Structure:**
1. **Header** (`px-4 py-4 border-b border-zinc-800`): Sub-lesson title + a close button (`X` icon, `text-zinc-500 hover:text-zinc-300`).
2. **Content Area** (`flex-1 overflow-y-auto px-5 py-4`): Render `WikiContent.content` through `react-markdown` with `className="prose prose-invert prose-sm max-w-none"`. Include key points as a clean list.
3. **User Notes Section** (`border-t border-zinc-800 px-4 py-4`): A simple textarea for personal notes with an "Add Note" button. Notes persist in component state.

**Animation:** Use `framer-motion` with `initial={{ x: 320, opacity: 0 }}`, `animate={{ x: 0, opacity: 1 }}`, `exit={{ x: 320, opacity: 0 }}`, `transition={{ duration: 0.25, ease: "easeOut" }}`.

---

## 6. ULTRA-MINIMALIST UI/UX RULES (NON-NEGOTIABLE)

These rules must be enforced across every component. Violating them breaks the design philosophy.

### Color System

| Element | Class |
|---|---|
| Left sidebar background | `bg-zinc-950` |
| Center canvas background | `bg-zinc-900` |
| Wiki drawer background | `bg-zinc-950` |
| Primary text | `text-zinc-100` |
| Secondary text | `text-zinc-400` |
| Muted/hint text | `text-zinc-600` |
| Borders | `border-zinc-800` |
| Hover backgrounds | `bg-zinc-800/50` or `bg-zinc-900/50` |
| User message bubble | `bg-zinc-800/50` |
| Input box | `bg-zinc-800/80` |

### Absolute Prohibitions

The following are **strictly forbidden** and must never appear in the codebase:

- Any `cyan`, `teal`, `purple`, `violet`, `blue`, or other accent colors.
- Any `box-shadow` with colored glows (e.g., `0 0 20px rgba(6, 182, 212, 0.3)`).
- Any `backdrop-blur` or `glassmorphism` effects.
- Any `background: linear-gradient(...)` with colored stops.
- Any top navigation bar or `TopBar` component.
- Any bouncy `spring` animations on UI elements.
- Any `text-shadow` or `drop-shadow` with colors.

### Animation Philosophy

Animations must be **functional, not decorative**. They communicate state changes, not personality.

- Transitions: `duration-150` or `duration-200`, `ease-out`.
- Message entrance: `opacity: 0 → 1`, `y: 8 → 0`, `duration: 0.25s`.
- Wiki drawer: slide in from right, `duration: 0.25s`.
- No bouncing, no spring physics on interactive elements.
- Hover states: color change only (`text-zinc-300`, `bg-zinc-800/50`). No scale transforms.

### Typography

- Font: `Inter` (system fallback acceptable). Import from Google Fonts.
- Body text: `text-sm leading-relaxed` (14px, 1.625 line height).
- Section labels: `text-xs font-medium text-zinc-500 uppercase tracking-wider`.
- Chat AI response: rendered through `prose prose-invert` — let Tailwind Typography handle all heading/list/code styles.

---

## 7. COMPONENT SPECIFICATIONS

### `QuizCard.tsx`

Academic multiple-choice style. No arcade aesthetics.

- Container: `bg-zinc-900 rounded-lg p-4 border border-zinc-800 mt-3`
- Question: `text-sm font-semibold text-zinc-100`
- Each option: a `<button>` with `flex items-start gap-3 px-3 py-2.5 rounded-lg border border-zinc-800 bg-zinc-950 hover:bg-zinc-900 transition-colors`
- Radio indicator: a `w-5 h-5 rounded-full border-2 border-zinc-600` with a filled inner circle when selected.
- After submission: correct options get `border-green-800/50 bg-green-900/20 text-green-300`; incorrect get `border-red-800/50 bg-red-900/20 text-red-300`.
- "Check Answer" button: `w-full px-4 py-2 bg-zinc-800 text-zinc-100 rounded-lg hover:bg-zinc-700 text-sm font-medium`.

### `OrcaLogo.tsx` (Custom SVG — REQUIRED)

Build a custom inline SVG logo. Do **not** use a generic icon from lucide-react. The logo must be an abstract geometric representation of either an orca dorsal fin or a continuous data loop. Requirements:

- `fill="none"`, `stroke="currentColor"`, `strokeWidth="1.5"`.
- Size: `w-5 h-5` or `w-6 h-6`.
- Strictly monochrome — inherits color from parent `text-*` class.
- Must feel like it belongs in a Linear.app or Raycast product.

Example SVG path concept (orca fin abstraction):
```svg
<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
  <!-- Rising fin curve -->
  <path d="M4 20 C4 20 8 4 12 3 C16 4 18 12 20 20" />
  <!-- Tail fluke -->
  <path d="M8 20 C10 17 14 17 16 20" />
</svg>
```

### `ThinkingIndicator.tsx`

Three animated dots in a row. No glows, no colors.

```tsx
<div className="flex items-center gap-1.5 py-2">
  {[0, 1, 2].map((i) => (
    <motion.div
      key={i}
      className="w-1.5 h-1.5 rounded-full bg-zinc-500"
      animate={{ opacity: [0.3, 1, 0.3] }}
      transition={{ duration: 1.2, repeat: Infinity, delay: i * 0.2 }}
    />
  ))}
  <span className="text-xs text-zinc-500 ml-2">{state}</span>
</div>
```

---

## 8. ROUTING & PAGES

Use `wouter` for client-side routing. Define these routes in `App.tsx`:

| Route | Component | Description |
|---|---|---|
| `/` | `Home` | Main 3-pane learning interface |
| `/profile` | `Profile` | User learning progress dashboard |
| `/history` | `QuizHistoryAndNotes` | Quiz history and personal notes |

The `Profile` page must include:
- Stats cards (streak, topics mastered, quizzes taken, accuracy rate).
- A GitHub-style activity heatmap (52 weeks × 7 days grid, colored by `bg-zinc-700` to `bg-zinc-300` intensity).
- Topic progress bars with mastery levels (Beginner → Intermediate → Advanced → Expert).
- Achievement badges with rarity levels.
- Tabs: Overview, Quiz History, Analytics, Goals.

### Navigation Between Pages

Since there is no top bar, navigation must happen through:
1. The user profile section at the bottom of the left sidebar (click avatar → Profile page).
2. A small "History" link in the sidebar footer.
3. The `wouter` `<Link>` component — never `<a href>` tags for internal navigation.

---

## 9. STATE MANAGEMENT

Use React Context for global state. Define these contexts:

**`QuizHistoryContext`** — Stores all `QuizAttempt[]` objects. Provides `addQuizAttempt(attempt)` and `attempts` to all components. Wrap the entire app in this provider.

**`ThemeContext`** — Manages dark/light/auto theme. Default is `dark`. The auto mode switches based on system preference. Provides `theme`, `setTheme`, and `toggleTheme`.

All other state (topics, messages, wiki open/closed) lives in the `Home` page component and is passed down as props.

---

## 10. MOCK DATA REQUIREMENTS

Pre-populate `mockData.ts` with:

**2 initial topics** with sub-lessons:
1. `"Python Fundamentals"` (4 sub-lessons: Variables, Control Flow, Functions, OOP)
2. `"Machine Learning"` (3 sub-lessons: Supervised Learning, Neural Networks, Model Evaluation)

**3 wiki content entries** with rich markdown:
- `"py-1"` — Variables & Data Types (with Python code blocks, type table)
- `"ml-1"` — Supervised Learning (with algorithm comparison table, code example)
- `"csg-1"` — C# Generics Introduction (with before/after code blocks)

**4 quiz questions** covering Python, C#, algorithms, and ML.

**Quick prompts** for the welcome state:
```typescript
export const QUICK_PROMPTS = [
  { icon: "🐍", label: "Python Basics", prompt: "I want to learn Python fundamentals" },
  { icon: "🧠", label: "Machine Learning", prompt: "Teach me about machine learning" },
  { icon: "⚡", label: "C# Generics", prompt: "I want to learn C# Generics /plan" },
  { icon: "💻", label: "Web Development", prompt: "I want to learn web development /plan" },
];
```

---

## 11. FILE STRUCTURE

```
client/src/
├── lib/
│   ├── types.ts          ← All TypeScript interfaces
│   ├── mockData.ts       ← AI engine, topics, wiki content, quiz pool
│   └── utils.ts          ← cn() utility
├── contexts/
│   ├── QuizHistoryContext.tsx
│   └── ThemeContext.tsx
├── components/
│   ├── OrcaLogo.tsx      ← Custom SVG logo (REQUIRED)
│   ├── LeftSidebar.tsx   ← Knowledge Map (w-64, zinc-950)
│   ├── ChatPanel.tsx     ← Center canvas (flex-1, zinc-900)
│   ├── WikiDrawer.tsx    ← Right panel (w-80, zinc-950, slide-in)
│   ├── ChatMessage.tsx   ← User + AI message rendering
│   ├── QuizCard.tsx      ← Academic multiple-choice quiz
│   └── ThinkingIndicator.tsx ← Animated dots
├── pages/
│   ├── Home.tsx          ← 3-pane layout orchestrator
│   ├── Profile.tsx       ← Learning progress dashboard
│   └── QuizHistoryAndNotes.tsx ← Quiz history + personal notes
├── App.tsx               ← Router + providers
├── main.tsx
└── index.css             ← Tailwind imports + prose customization
```

---

## 12. CSS & TAILWIND CONFIGURATION

In `index.css`, configure the `prose-invert` styles to match the zinc palette:

```css
@import "tailwindcss";
@import "@tailwindcss/typography";

:root {
  color-scheme: dark;
}

body {
  background-color: theme(colors.zinc.950);
  color: theme(colors.zinc.100);
  font-family: 'Inter', system-ui, sans-serif;
}
```

In `index.html`, add the Google Fonts import for Inter:
```html
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap" rel="stylesheet" />
```

Also install `@tailwindcss/typography` for the `prose` classes:
```bash
pnpm add @tailwindcss/typography
```

---

## 13. FINAL QUALITY CHECKLIST

Before delivering, verify every item:

- [ ] `h-screen flex overflow-hidden` on root — no page scroll.
- [ ] No `TopBar` component exists anywhere.
- [ ] AI messages render markdown (bold, lists, code blocks) — not raw `**text**` strings.
- [ ] Chat messages are constrained to `max-w-3xl mx-auto` — not full width.
- [ ] Input area is sticky to the bottom of the center canvas.
- [ ] Wiki drawer slides in from the right without covering the sidebar.
- [ ] No cyan, teal, purple, or any accent colors anywhere.
- [ ] No `box-shadow` with colored glows.
- [ ] `OrcaLogo` is a custom SVG, not a lucide icon.
- [ ] `/plan` command adds a new topic to the sidebar.
- [ ] Quiz cards are academic style (radio buttons, clean borders).
- [ ] `QuizHistoryContext` saves all quiz attempts.
- [ ] Profile page is accessible from the sidebar user section.
- [ ] TypeScript: zero errors (`npx tsc --noEmit`).
