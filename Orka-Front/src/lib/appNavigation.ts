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
    key: "tutor",
    view: "tutor",
    path: "/app/tutor",
    label: "Koç",
    icon: MessageSquare,
    accent: "#6ed7ce",
    description: "Soru sor, kavramı açtır, konuyu konuş",
  },
  {
    key: "home",
    view: "home",
    path: "/app",
    label: "Planlar",
    icon: Home,
    accent: "#a7e879",
    description: "Müfredat ve çalışma yollarını gör",
  },
  {
    key: "sources-wiki",
    view: "sources-wiki",
    path: "/app/sources",
    label: "Wiki",
    icon: BookOpen,
    accent: "#b4a0f0",
    description: "Kaynak defteri ve bilgi tabanı",
  },
  {
    key: "notebook",
    view: "notebook",
    path: "/app/notebook",
    label: "OrkaLM",
    icon: FileStack,
    accent: "#dac17a",
    description: "Üretim stüdyosu ve analiz aracı",
  },
  {
    key: "settings",
    view: "settings",
    path: "/app/settings",
    label: "Ayarlar",
    icon: Settings,
    accent: "#8f9894",
    description: "Güvenlik, dil ve hesap ayarları",
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
