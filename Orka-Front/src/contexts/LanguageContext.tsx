import React, { createContext, useContext, useEffect, useMemo, useState } from "react";
import { UserAPI } from "@/services/api";
import { LANGUAGE_OPTIONS, normalizeLocale, type Locale } from "@/i18n/languages";
import { messages } from "@/i18n/messages";

export type Language = Locale;

interface LanguageContextType {
  language: Language;
  locale: Locale;
  languages: typeof LANGUAGE_OPTIONS;
  setLanguage: (lang: Language) => void;
  t: (key: string, params?: Record<string, string | number>) => string;
}

const LanguageContext = createContext<LanguageContextType | undefined>(undefined);

const interpolate = (template: string, params?: Record<string, string | number>) => {
  if (!params) return template;
  return Object.entries(params).reduce(
    (text, [key, value]) => text.replaceAll(`{${key}}`, String(value)),
    template,
  );
};

export function LanguageProvider({ children }: { children: React.ReactNode }) {
  const [language, setLanguageState] = useState<Language>(() =>
    normalizeLocale(localStorage.getItem("orka_language")),
  );

  useEffect(() => {
    const token = localStorage.getItem("orka_token");
    if (!token) return;

    UserAPI.getMe()
      .then((res) => {
        if (res.data?.settings?.language) {
          const lang = normalizeLocale(res.data.settings.language);
          setLanguageState(lang);
          localStorage.setItem("orka_language", lang);
        }
      })
      .catch(() => {});
  }, []);

  const setLanguage = (lang: Language) => {
    const next = normalizeLocale(lang);
    setLanguageState(next);
    localStorage.setItem("orka_language", next);
  };

  const value = useMemo<LanguageContextType>(() => {
    const t = (key: string, params?: Record<string, string | number>) => {
      const template =
        messages[language]?.[key] ??
        messages.en?.[key] ??
        messages.tr?.[key] ??
        key;
      return interpolate(template, params);
    };

    return {
      language,
      locale: language,
      languages: LANGUAGE_OPTIONS,
      setLanguage,
      t,
    };
  }, [language]);

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage() {
  const ctx = useContext(LanguageContext);
  if (!ctx) throw new Error("useLanguage must be used within LanguageProvider");
  return ctx;
}
