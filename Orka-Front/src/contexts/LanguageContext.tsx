/**
 * LanguageContext — Türkçe / English dil yönetimi (encoding sorunsuz).
 * Tüm UI string'leri bu context'ten çekilir.
 */
import React, { createContext, useContext, useState, useEffect } from "react";
import { UserAPI } from "@/services/api";

export type Language = "English" | "Türkçe";

interface LanguageContextType {
  language: Language;
  setLanguage: (lang: Language) => void;
  t: (key: string) => string;
}

const translations: Record<Language, Record<string, string>> = {
  English: {
    settings: "Settings",
    profile: "Profile",
    language_region: "Language & Region",
    theme: "Theme",
    notifications: "Notifications",
    delete_account: "Delete account completely",
    light: "Light",
    dark: "Dark",
    system: "System",
    interface_language: "Interface Language",
    edit: "Edit",
    wiki: "Knowledge Wiki",
    table_of_contents: "Table of Contents",
    topics: "Topics",
    font_size: "Font Size",
    small: "Small",
    medium: "Medium",
    large: "Large",
    appearance: "Appearance",
    account_security: "Account & Security",
    quiz_reminders: "Quiz reminders",
    quiz_reminders_desc: "Get daily practice reminders",
    weekly_report: "Weekly progress report",
    weekly_report_desc: "Receive a summary of your learning",
    new_content: "New content alerts",
    new_content_desc: "Notify me when new wiki content is created",
    sounds: "Sound effects",
    sounds_desc: "Play sounds for quiz answers and notifications",
    // Sidebar nav
    home_nav: "Home",
    courses: "Courses",
    chat_history: "Chat History",
  },
  Türkçe: {
    settings: "Ayarlar",
    profile: "Profil",
    language_region: "Dil ve Bölge",
    theme: "Tema",
    notifications: "Bildirimler",
    delete_account: "Hesabı tamamen sil",
    light: "Açık",
    dark: "Koyu",
    system: "Sistem",
    interface_language: "Arayüz Dili",
    edit: "Düzenle",
    wiki: "Bilgi Wiki",
    table_of_contents: "İçindekiler",
    topics: "Konular",
    font_size: "Yazı Boyutu",
    small: "Küçük",
    medium: "Orta",
    large: "Büyük",
    appearance: "Görünüm",
    account_security: "Hesap ve Güvenlik",
    quiz_reminders: "Quiz hatırlatıcı",
    quiz_reminders_desc: "Günlük pratik yapmak için hatırlatmalar alın",
    weekly_report: "Haftalık gelişim raporu",
    weekly_report_desc: "Öğrenme sürecinizin özetini alın",
    new_content: "Yeni içerik uyarıları",
    new_content_desc: "Yeni wiki içeriği oluştuğunda bildir",
    sounds: "Ses efektleri",
    sounds_desc: "Quiz cevapları ve bildirimler için ses çal",
    // Sidebar nav
    home_nav: "Anasayfa",
    courses: "Kurslar",
    chat_history: "Sohbet Geçmişi",
  },
};

const LanguageContext = createContext<LanguageContextType | undefined>(undefined);

export function LanguageProvider({ children }: { children: React.ReactNode }) {
  const [language, setLanguageState] = useState<Language>(() => {
    return (localStorage.getItem("orka_language") as Language) || "Türkçe";
  });

  useEffect(() => {
    const token = localStorage.getItem("orka_token");
    if (!token) return;

    UserAPI.getMe().then((res) => {
      if (res.data?.settings?.language) {
        const lang = res.data.settings.language as Language;
        setLanguageState(lang);
        localStorage.setItem("orka_language", lang);
      }
    }).catch(() => {});
  }, []);

  const setLanguage = (lang: Language) => {
    setLanguageState(lang);
    localStorage.setItem("orka_language", lang);
  };

  const t = (key: string) => {
    return translations[language]?.[key] ?? key;
  };

  return (
    <LanguageContext.Provider value={{ language, setLanguage, t }}>
      {children}
    </LanguageContext.Provider>
  );
}

export function useLanguage() {
  const ctx = useContext(LanguageContext);
  if (!ctx) throw new Error("useLanguage must be used within LanguageProvider");
  return ctx;
}
