/*
 * SettingsPanel — Tema, Dil, Font, Bildirimler, Hesap.
 * - Tema/Font değişikliği anında DOM'a uygulanır (ThemeContext, FontSizeContext).
 * - Dil değişikliği anında UI'ya yansır (LanguageContext).
 * - Tüm ayarlar backend'e PATCH /user/settings ile kaydedilir.
 * - Backend key'leri İngilizce sabit (Dark/Light/System, Small/Medium/Large, English/Türkçe).
 * - Profil (Ad/Soyad) PATCH /user/profile ile güncellenir.
 */

import { useState, useEffect } from "react";
import { toast } from "react-hot-toast";
import { UserAPI, type AuthUser } from "../services/api";
import { useLocation } from "wouter";
import { useLanguage, type Language } from "../contexts/LanguageContext";
import { useTheme, type Theme } from "../contexts/ThemeContext";
import { useFontSize, type FontSize } from "../contexts/FontSizeContext";
import {
  User,
  Bell,
  Palette,
  Globe,
  Shield,
  Check,
} from "lucide-react";
import OrcaLogo from "./OrcaLogo";

// ── Sub-components ─────────────────────────────────────────────────────────

function SettingsSection({ title, icon, children }: {
  title: string;
  icon: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="mb-6">
      <div className="flex items-center gap-2.5 mb-4">
        <span className="text-[#667085]">{icon}</span>
        <h3 className="text-sm font-semibold text-[#172033]">{title}</h3>
      </div>
      <div className="space-y-1">{children}</div>
    </div>
  );
}

function ToggleRow({ label, description, checked, onChange }: {
  label: string;
  description?: string;
  checked: boolean;
  onChange: (val: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between px-4 py-3 rounded-xl hover:bg-[#dcecf3]/70/30 transition-colors duration-150">
      <div>
        <p className="text-sm text-[#344054]">{label}</p>
        {description && <p className="text-[11px] text-[#667085] mt-0.5">{description}</p>}
      </div>
      <button
        onClick={() => onChange(!checked)}
        className={`rounded-full transition-all duration-250 relative flex-shrink-0 focus:outline-none ${
          checked ? "bg-zinc-400" : "bg-zinc-700"
        }`}
        style={{ height: "22px", width: "40px" }}
        aria-checked={checked}
        role="switch"
      >
        <div
          className={`w-4 h-4 rounded-full bg-[#172033] absolute top-[3px] transition-transform duration-200 shadow-sm ${
            checked ? "translate-x-[20px]" : "translate-x-[3px]"
          }`}
        />
      </button>
    </div>
  );
}

function OptionRow<T extends string>({ label, value, options, onChange }: {
  label: string;
  value: T;
  options: { key: T; label: string }[];
  onChange: (val: T) => void;
}) {
  return (
    <div className="px-4 py-3 rounded-xl hover:bg-[#dcecf3]/70/30 transition-colors duration-150">
      <p className="text-sm text-[#344054] mb-2.5">{label}</p>
      <div className="flex gap-2 flex-wrap">
        {options.map((opt) => (
          <button
            key={opt.key}
            onClick={() => onChange(opt.key)}
            className={`flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-medium transition-all duration-200 border ${
              value === opt.key
                ? "bg-[#172033] text-white border-transparent shadow-sm"
                : "bg-[#dcecf3]/62 text-[#667085] border-[#526d82]/18/50 hover:text-[#172033] hover:border-zinc-600"
            }`}
          >
            {value === opt.key && <Check className="w-3 h-3" />}
            {opt.label}
          </button>
        ))}
      </div>
    </div>
  );
}

// ── Main Component ─────────────────────────────────────────────────────────

export default function SettingsPanel() {
  const [, navigate] = useLocation();
  const { t, language, setLanguage } = useLanguage();
  const { theme, setTheme } = useTheme();
  const { fontSize, setFontSize } = useFontSize();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [saving, setSaving] = useState(false);
  const [showConfirmDelete, setShowConfirmDelete] = useState(false);

  const [notifications, setNotifications] = useState({
    quizReminders: true,
    weeklyReport: true,
    newContent: false,
    sounds: true,
  });

  const [profile, setProfile] = useState({
    firstName: "",
    lastName: "",
  });

  // Load user settings from backend on mount
  useEffect(() => {
    UserAPI.getMe().then((res) => {
      setUser(res.data);
      setProfile({
        firstName: res.data.firstName || "",
        lastName: res.data.lastName || "",
      });
      const s = res.data.settings;
      if (!s) return;

      if (s.fontSize) setFontSize(s.fontSize as FontSize);

      setNotifications({
        quizReminders: s.quizReminders ?? true,
        weeklyReport: s.weeklyReport ?? true,
        newContent: s.newContentAlerts ?? false,
        sounds: s.soundsEnabled ?? true,
      });
    }).catch(() => {});
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Persist setting change to backend
  const persist = async (payload: Record<string, unknown>) => {
    if (saving) return;
    setSaving(true);
    try {
      await UserAPI.updateSettings(payload);
      toast.success(t("settings") + " kaydedildi", { duration: 1500, icon: "✓" });
    } catch {
      toast.error("Ayar güncellenemedi");
    } finally {
      setSaving(false);
    }
  };

  const handleProfileChange = async () => {
    if (saving || !user) return;
    if (profile.firstName === user.firstName && profile.lastName === user.lastName) return;

    setSaving(true);
    try {
      await UserAPI.updateProfile({ firstName: profile.firstName, lastName: profile.lastName });
      toast.success("Profil başarıyla güncellendi", { duration: 1500, icon: "✓" });
      setUser({ ...user, firstName: profile.firstName, lastName: profile.lastName });
    } catch {
      toast.error("Profil güncellenemedi");
    } finally {
      setSaving(false);
    }
  };

  const handleTheme = (val: Theme) => {
    setTheme(val);
    persist({ theme: val });
  };

  const handleLanguage = (val: Language) => {
    setLanguage(val);
    persist({ language: val });
  };

  const handleFontSize = (val: FontSize) => {
    setFontSize(val);
    persist({ fontSize: val });
  };

  const handleNotification = (key: string, val: boolean) => {
    const newNotifs = { ...notifications, [key]: val };
    setNotifications(newNotifs);
    persist({
      quizReminders: newNotifs.quizReminders,
      weeklyReport: newNotifs.weeklyReport,
      newContentAlerts: newNotifs.newContent,
      soundsEnabled: newNotifs.sounds,
    });
  };

  const handleDeleteAccount = async () => {
    try {
      await UserAPI.deleteAccount();
      localStorage.clear();
      navigate("/login");
      toast.success("Hesabınız silindi.");
    } catch {
      toast.error("Hesap silinemedi");
    }
  };

  // Localized option lists
  const fontOptions: { key: FontSize; label: string }[] = [
    { key: "Small", label: t("small") || "Küçük" },
    { key: "Medium", label: t("medium") || "Orta" },
    { key: "Large", label: t("large") || "Büyük" },
  ];

  return (
    <div className="flex-1 flex flex-col bg-transparent h-full overflow-hidden">
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-2xl mx-auto w-full px-6 py-8">

          {/* Header */}
          <div className="mb-8">
            <h1 className="text-xl font-bold text-[#172033] mb-1">{t("settings")}</h1>
            <p className="text-sm text-[#667085]">Tercihlerinizi ve profilinizi yönetin</p>
          </div>

          {/* ── PROFILE ── */}
          <SettingsSection title={t("profile") || "Profil"} icon={<User className="w-4 h-4" />}>
            {/* Avatar + Info Row */}
            <div className="px-4 py-4 rounded-xl bg-[#dcecf3]/70/30 border border-[#526d82]/14">
              <div className="flex items-center gap-4 mb-4">
                <div className="w-14 h-14 rounded-full bg-[#dcecf3]/70 border border-[#526d82]/18 flex items-center justify-center">
                  <span className="text-lg font-semibold text-[#344054]">
                    {profile.firstName?.[0]?.toUpperCase() || user?.firstName?.[0]?.toUpperCase() || "U"}
                  </span>
                </div>
                <div className="flex-1">
                  <p className="text-sm font-medium text-[#172033]">
                    {user ? `${user.firstName} ${user.lastName}`.trim() : "Yükleniyor..."}
                  </p>
                  <p className="text-xs text-[#667085] mt-0.5">{user?.email}</p>
                </div>
                <span className={`text-[10px] font-semibold px-2 py-1 rounded-md ${
                  user?.plan === "Pro"
                    ? "bg-amber-500/15 text-amber-400 border border-amber-500/30"
                    : "bg-[#dcecf3]/70 text-[#667085] border border-[#526d82]/18"
                }`}>
                  {user?.plan || "Free"}
                </span>
              </div>

              {/* Editable Name Fields */}
              <div className="flex flex-col gap-1.5 mt-3">
                <label className="text-xs text-[#667085] font-medium">Kullanıcı Adı</label>
                <input
                  type="text"
                  value={profile.firstName}
                  onChange={(e) => setProfile({ ...profile, firstName: e.target.value })}
                  onBlur={handleProfileChange}
                  className="bg-[#f7f9fa]/62 border border-[#526d82]/22 rounded-lg px-3 py-2 text-sm text-[#172033] outline-none focus:border-zinc-500 transition-colors"
                  placeholder="Kullanıcı adı girin"
                />
              </div>

              {/* Email (readonly) */}
              <div className="flex flex-col gap-1.5 mt-3">
                <label className="text-xs text-[#667085] font-medium">E-posta (Değiştirilemez)</label>
                <input
                  type="email"
                  value={user?.email || ""}
                  disabled
                  className="bg-[#f7f9fa]/62 border border-[#526d82]/18 opacity-60 rounded-lg px-3 py-2 text-sm text-[#667085] outline-none cursor-not-allowed"
                />
              </div>
            </div>
          </SettingsSection>

          <div className="border-t border-[#526d82]/14 mb-6" />

          {/* ── APPEARANCE ── */}
          <SettingsSection title={t("appearance") || "Görünüm"} icon={<Palette className="w-4 h-4" />}>
            <OptionRow<FontSize>
              label={t("font_size") || "Yazı Boyutu"}
              value={fontSize}
              options={fontOptions}
              onChange={handleFontSize}
            />
          </SettingsSection>

          <div className="border-t border-[#526d82]/14 mb-6" />

          {/* ── ACCOUNT & SECURITY ── */}
          <SettingsSection title={t("account_security") || "Hesap ve Güvenlik"} icon={<Shield className="w-4 h-4" />}>
            <div className="px-4 py-3 text-[12px] text-[#667085] leading-relaxed">
              Hesabınızı sildiğinizde tüm sohbet geçmişiniz, oluşturduğunuz planlar ve wiki sayfaları kalıcı olarak silinecektir. Bu işlem geri alınamaz.
            </div>
            <div className="px-4 pb-2">
              {!showConfirmDelete ? (
                <button
                  onClick={() => setShowConfirmDelete(true)}
                  className="w-full px-4 py-3 rounded-xl text-sm font-semibold text-red-400 bg-red-500/5 hover:bg-red-500/10 border border-red-500/20 transition-all duration-200 text-center"
                >
                  {t("delete_account") || "Hesabımı Kalıcı Sil"}
                </button>
              ) : (
                <div className="flex flex-col gap-3 p-4 rounded-xl border border-red-500/30 bg-red-500/10">
                   <p className="text-sm text-red-200 text-center font-medium">Bu işlem geri alınamaz. Emin misiniz?</p>
                   <div className="flex gap-2">
                     <button
                        onClick={() => setShowConfirmDelete(false)}
                        className="flex-1 px-4 py-2 rounded-lg text-sm font-medium text-[#344054] bg-[#dcecf3]/70 hover:bg-zinc-700 transition-colors"
                     >
                        İptal
                     </button>
                     <button
                        onClick={handleDeleteAccount}
                        id="confirm-delete-btn"
                        className="flex-1 px-4 py-2 rounded-lg text-sm font-medium text-white bg-red-600 hover:bg-red-500 transition-colors"
                     >
                        Evet, Sil
                     </button>
                   </div>
                </div>
              )}
            </div>
          </SettingsSection>

          {/* Footer */}
          <div className="mt-8 pt-6 border-t border-[#526d82]/14 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <OrcaLogo className="w-4 h-4 text-[#98a2b3]" />
              <span className="text-[11px] text-[#98a2b3]">Orka AI v1.0</span>
            </div>
            <div className="flex gap-4">
              <button className="text-[11px] text-[#98a2b3] hover:text-[#667085] transition-colors">
                Privacy Policy
              </button>
              <button className="text-[11px] text-[#98a2b3] hover:text-[#667085] transition-colors">
                Terms of Service
              </button>
            </div>
          </div>

        </div>
      </div>
    </div>
  );
}
