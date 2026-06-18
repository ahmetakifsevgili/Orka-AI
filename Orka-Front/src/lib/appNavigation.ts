import {
  BookOpen,
  BrainCircuit,
  ClipboardCheck,
  Code2,
  FileStack,
  GraduationCap,
  Home,
  MessageSquare,
  Settings,
  Trophy,
  type LucideIcon,
} from "lucide-react";

export const CANONICAL_APP_VIEWS = [
  "home",
  "tutor",
  "study-room",
  "review",
  "exams",
  "sources-wiki",
  "notebook",
  "code",
  "progress",
  "settings",
] as const;

export type CanonicalAppView = (typeof CANONICAL_APP_VIEWS)[number];

export type AppView =
  | CanonicalAppView
  | "chat"
  | "dashboard"
  | "learning"
  | "practice"
  | "central-exams"
  | "wiki"
  | "sources"
  | "orkalm"
  | "ide";

export type AppNavItem = {
  key: CanonicalAppView;
  view: CanonicalAppView;
  path: string;
  label: string;
  icon: LucideIcon;
  accent: string;
  description: string;
};

export const APP_NAV_ITEMS: readonly AppNavItem[] = [
  {
    key: "home",
    view: "home",
    path: "/app",
    label: "Ana Kokpit",
    icon: Home,
    accent: "#6ed7ce",
    description: "Bugunun en dogru calisma adimini gor",
  },
  {
    key: "tutor",
    view: "tutor",
    path: "/app/tutor",
    label: "Tutor",
    icon: MessageSquare,
    accent: "#a7e879",
    description: "Soru sor, kavrami actir, konuyu konus",
  },
  {
    key: "study-room",
    view: "study-room",
    path: "/app/study-room",
    label: "Study Room",
    icon: GraduationCap,
    accent: "#b4a0f0",
    description: "Ders, ornek ve kisa kontrol akisi",
  },
  {
    key: "review",
    view: "review",
    path: "/app/review",
    label: "Review / Quiz",
    icon: ClipboardCheck,
    accent: "#dac17a",
    description: "Tekrar, quiz ve zayif kavram onarimi",
  },
  {
    key: "exams",
    view: "exams",
    path: "/app/exams",
    label: "Exam War Room",
    icon: Trophy,
    accent: "#d98282",
    description: "Merkezi sinav hazirligi ve deneme akisi",
  },
  {
    key: "sources-wiki",
    view: "sources-wiki",
    path: "/app/sources",
    label: "Sources / Wiki",
    icon: BookOpen,
    accent: "#6ed7ce",
    description: "Kaynak defteri, wiki ve citation dayanaklari",
  },
  {
    key: "notebook",
    view: "notebook",
    path: "/app/notebook",
    label: "Notebook Studio",
    icon: FileStack,
    accent: "#a7e879",
    description: "Kaynakli ozet, quiz, audio ve cikti paketi",
  },
  {
    key: "code",
    view: "code",
    path: "/app/code",
    label: "Code IDE",
    icon: Code2,
    accent: "#b4a0f0",
    description: "Kod pratigi ve tutor handoff",
  },
  {
    key: "progress",
    view: "progress",
    path: "/app/progress",
    label: "Progress",
    icon: BrainCircuit,
    accent: "#6ed7ce",
    description: "Zayif kavram, bellek ve ilerleme sinyalleri",
  },
  {
    key: "settings",
    view: "settings",
    path: "/app/settings",
    label: "Settings / Safety",
    icon: Settings,
    accent: "#8f9894",
    description: "Guvenlik, dil ve hesap ayarlari",
  },
] as const;

const LEGACY_VIEW_ALIASES: Record<string, CanonicalAppView> = {
  dashboard: "home",
  chat: "tutor",
  classroom: "study-room",
  learning: "review",
  practice: "review",
  "central-exams": "exams",
  wiki: "sources-wiki",
  sources: "sources-wiki",
  orkalm: "notebook",
  ide: "code",
};

export function normalizeAppView(view: string | null | undefined): CanonicalAppView {
  if (!view) return "home";
  if ((CANONICAL_APP_VIEWS as readonly string[]).includes(view)) return view as CanonicalAppView;
  return LEGACY_VIEW_ALIASES[view] ?? "home";
}

export function appViewPath(view: string | null | undefined): string {
  const normalized = normalizeAppView(view);
  return APP_NAV_ITEMS.find((item) => item.view === normalized)?.path ?? "/app";
}

export function isKnownAppView(view: string | null | undefined): view is AppView {
  if (!view) return false;
  return (CANONICAL_APP_VIEWS as readonly string[]).includes(view) || Object.prototype.hasOwnProperty.call(LEGACY_VIEW_ALIASES, view);
}

export function appViewLabels(): string[] {
  return APP_NAV_ITEMS.map((item) => item.label);
}
