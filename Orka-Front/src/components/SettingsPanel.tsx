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
import {
  UserAPI,
  type AuthUser,
  type EducationLevel,
  type LearningGoal,
  type LearningTone,
} from "../services/api";
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
  GraduationCap,
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
        <span className="soft-text-muted">{icon}</span>
        <h3 className="text-sm font-semibold text-foreground">{title}</h3>
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
    <div className="flex items-center justify-between px-4 py-3 rounded-xl hover:bg-surface-muted transition-colors duration-150">
      <div>
        <p className="text-sm text-foreground">{label}</p>
        {description && <p className="text-[11px] soft-text-muted mt-0.5">{description}</p>}
      </div>
      <button
        onClick={() => onChange(!checked)}
        className={`rounded-full transition-all duration-250 relative flex-shrink-0 focus:outline-none ${
          checked ? "bg-emerald-500" : "soft-muted"
        }`}
        style={{ height: "22px", width: "40px" }}
        aria-checked={checked}
        role="switch"
      >
        <div
          className={`w-4 h-4 rounded-full bg-surface absolute top-[3px] transition-transform duration-200 shadow-sm ${
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
    <div className="px-4 py-3 rounded-xl hover:bg-surface-muted transition-colors duration-150">
      <p className="text-sm text-foreground mb-2.5">{label}</p>
      <div className="flex gap-2 flex-wrap">
        {options.map((opt) => (
          <button
            key={opt.key}
            onClick={() => onChange(opt.key)}
            className={`flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-medium transition-all duration-200 border ${
              value === opt.key
                ? "bg-foreground text-background border-transparent"
                : "soft-muted soft-text-muted border-soft-border hover:text-foreground"
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

  // Öğrenme Profili (Faz B — mevcut kullanıcılar için doldurma)
  const [learning, setLearning] = useState<{
    age: number;
    educationLevel: EducationLevel;
    learningGoal: LearningGoal;
    learningTone: LearningTone;
    dailyStudyMinutes: number;
  }>({
    age: 18,
    educationLevel: "Unknown",
    learningGoal: "Unknown",
    learningTone: "Friendly",
    dailyStudyMinutes: 30,
  });
  const [learningDirty, setLearningDirty] = useState(false);
  const [learningCompleted, setLearningCompleted] = useState(false);

  // Backend enum → string eşleme (API bazen int döndürebilir)
  const EDU_MAP: EducationLevel[] = ["Unknown", "Primary", "Secondary", "HighSchool", "University", "Graduate", "Professional"];
  const GOAL_MAP: LearningGoal[] = ["Unknown", "ExamPrep", "Career", "Hobby", "Academic", "Certification"];
  const TONE_MAP: LearningTone[] = ["Unknown", "Formal", "Friendly", "Playful"];
  const toEnum = <T extends string>(val: unknown, map: T[], fallback: T): T => {
    if (typeof val === "number" && val >= 0 && val < map.length) return map[val];
    if (typeof val === "string" && (map as string[]).includes(val)) return val as T;
    return fallback;
  };

  // Load user settings from backend on mount
  useEffect(() => {
    UserAPI.getMe().then((res) => {
      setUser(res.data);
      setProfile({
        firstName: res.data.firstName || "",
        lastName: res.data.lastName || "",
      });

      const lp = res.data.learningProfile;
      if (lp) {
        setLearning({
          age: typeof lp.age === "number" ? lp.age : 18,
          educationLevel: toEnum(lp.educationLevel, EDU_MAP, "Unknown"),
          learningGoal: toEnum(lp.learningGoal, GOAL_MAP, "Unknown"),
          learningTone: toEnum(lp.learningTone, TONE_MAP, "Friendly"),
          dailyStudyMinutes: typeof lp.dailyStudyMinutes === "number" ? lp.dailyStudyMinutes : 30,
        });
        setLearningCompleted(Boolean(lp.profileCompleted));
      }

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

  const handleLearningSave = async () => {
    if (saving) return;
    setSaving(true);
    try {
      await UserAPI.updateLearningProfile({
        age: learning.age,
        educationLevel: learning.educationLevel === "Unknown" ? null : learning.educationLevel,
        learningGoal: learning.learningGoal === "Unknown" ? null : learning.learningGoal,
        learningTone: learning.learningTone === "Unknown" ? null : learning.learningTone,
        dailyStudyMinutes: learning.dailyStudyMinutes,
      });
      setLearningDirty(false);
      setLearningCompleted(true);
      toast.success("Öğrenme profili kaydedildi", { duration: 1500, icon: "✓" });
    } catch {
      toast.error("Profil güncellenemedi");
    } finally {
      setSaving(false);
    }
  };

  const patchLearning = <K extends keyof typeof learning>(key: K, val: typeof learning[K]) => {
    setLearning((prev) => ({ ...prev, [key]: val }));
    setLearningDirty(true);
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
    <div className="flex-1 flex flex-col soft-page h-full overflow-hidden">
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-2xl mx-auto w-full px-6 py-8">

          {/* Header */}
          <div className="mb-8">
            <h1 className="text-xl font-bold text-foreground mb-1">{t("settings")}</h1>
            <p className="text-sm soft-text-muted">Tercihlerinizi ve profilinizi yönetin</p>
          </div>

          {/* ── PROFILE ── */}
          <SettingsSection title={t("profile") || "Profil"} icon={<User className="w-4 h-4" />}>
            {/* Avatar + Info Row */}
            <div className="px-4 py-4 rounded-xl soft-surface border">
              <div className="flex items-center gap-4 mb-4">
                <div className="w-14 h-14 rounded-full soft-muted border soft-border flex items-center justify-center">
                  <span className="text-lg font-semibold text-foreground">
                    {profile.firstName?.[0]?.toUpperCase() || user?.firstName?.[0]?.toUpperCase() || "U"}
                  </span>
                </div>
                <div className="flex-1">
                  <p className="text-sm font-medium text-foreground">
                    {user ? `${user.firstName} ${user.lastName}`.trim() : "Yükleniyor..."}
                  </p>
                  <p className="text-xs soft-text-muted mt-0.5">{user?.email}</p>
                </div>
                <span className={`text-[10px] font-semibold px-2 py-1 rounded-md ${
                  user?.plan === "Pro"
                    ? "bg-amber-500/15 text-amber-400 border border-amber-500/30"
                    : "soft-muted soft-text-muted border soft-border"
                }`}>
                  {user?.plan || "Free"}
                </span>
              </div>

              {/* Editable Name Fields */}
              <div className="flex flex-col gap-1.5 mt-3">
                <label className="text-xs soft-text-muted font-medium">Kullanıcı Adı</label>
                <input
                  type="text"
                  value={profile.firstName}
                  onChange={(e) => setProfile({ ...profile, firstName: e.target.value })}
                  onBlur={handleProfileChange}
                  className="soft-surface border rounded-lg px-3 py-2 text-sm outline-none focus:border-emerald-500/50 transition-colors"
                  placeholder="Kullanıcı adı girin"
                />
              </div>

              {/* Email (readonly) */}
              <div className="flex flex-col gap-1.5 mt-3">
                <label className="text-xs soft-text-muted font-medium">E-posta (Değiştirilemez)</label>
                <input
                  type="email"
                  value={user?.email || ""}
                  disabled
                  className="soft-muted border soft-border opacity-60 rounded-lg px-3 py-2 text-sm soft-text-muted outline-none cursor-not-allowed"
                />
              </div>
            </div>
          </SettingsSection>

          <div className="border-t soft-border mb-6" />

          {/* ── APPEARANCE ── */}
          <SettingsSection title={t("appearance") || "Görünüm"} icon={<Palette className="w-4 h-4" />}>
            <OptionRow<Theme>
              label="Tema"
              value={theme}
              options={[
                { key: "Light", label: "Soft Light" },
                { key: "Dark", label: "Grafit" },
                { key: "System", label: "Sistem" },
              ]}
              onChange={handleTheme}
            />
            <OptionRow<FontSize>
              label={t("font_size") || "Yazı Boyutu"}
              value={fontSize}
              options={fontOptions}
              onChange={handleFontSize}
            />
          </SettingsSection>

          <div className="border-t soft-border mb-6" />

          {/* ── LEARNING PROFILE (Faz B) ── */}
          <SettingsSection title="Öğrenme Profili" icon={<GraduationCap className="w-4 h-4" />}>
            <div className="px-4 py-4 rounded-xl soft-surface border space-y-5">
              <div className="flex items-start gap-3">
                <p className="text-[12px] soft-text-muted leading-relaxed flex-1">
                  Bu bilgiler müfredat derinliğini ve AI'ın anlatım üslubunu seninle uyumlu hâle getirir. Sınav (KPSS/YKS) veya kariyer odaklıysan sistem daha derin plan üretir.
                </p>
                {learningCompleted && !learningDirty && (
                  <span className="text-[10px] font-semibold px-2 py-1 rounded-md bg-emerald-500/15 text-emerald-400 border border-emerald-500/30 flex items-center gap-1 flex-shrink-0">
                    <Check className="w-3 h-3" /> Tamam
                  </span>
                )}
              </div>

              {/* Yaş */}
              <div>
                <label className="block text-xs font-medium text-zinc-400 mb-1.5">Yaşın</label>
                <div className="flex items-center gap-3">
                  <input
                    type="range"
                    min={6}
                    max={80}
                    value={learning.age}
                    onChange={(e) => patchLearning("age", parseInt(e.target.value, 10))}
                    className="flex-1 accent-emerald-500"
                  />
                  <span className="w-10 text-right text-sm text-zinc-200 tabular-nums">
                    {learning.age}
                  </span>
                </div>
              </div>

              {/* Eğitim */}
              <div>
                <label className="block text-xs font-medium text-zinc-400 mb-1.5">Eğitim Seviyen</label>
                <select
                  value={learning.educationLevel}
                  onChange={(e) => patchLearning("educationLevel", e.target.value as EducationLevel)}
                  className="w-full px-3 py-2.5 bg-zinc-950/50 border border-zinc-700/80 rounded-lg text-sm text-zinc-100 outline-none focus:border-zinc-500 transition-colors"
                >
                  <option value="Unknown">Belirtmek istemiyorum</option>
                  <option value="Primary">İlkokul</option>
                  <option value="Secondary">Ortaokul</option>
                  <option value="HighSchool">Lise</option>
                  <option value="University">Üniversite</option>
                  <option value="Graduate">Mezun / Yüksek Lisans</option>
                  <option value="Professional">Çalışan / Doktora</option>
                </select>
              </div>

              {/* Amaç */}
              <div>
                <label className="block text-xs font-medium text-zinc-400 mb-1.5">Öğrenme Amacın</label>
                <select
                  value={learning.learningGoal}
                  onChange={(e) => patchLearning("learningGoal", e.target.value as LearningGoal)}
                  className="w-full px-3 py-2.5 bg-zinc-950/50 border border-zinc-700/80 rounded-lg text-sm text-zinc-100 outline-none focus:border-zinc-500 transition-colors"
                >
                  <option value="Unknown">Genel öğrenme</option>
                  <option value="ExamPrep">Sınav hazırlığı (KPSS/YKS/ALES)</option>
                  <option value="Career">Kariyer / meslek</option>
                  <option value="Hobby">Hobi / kişisel ilgi</option>
                  <option value="Academic">Akademik çalışma</option>
                  <option value="Certification">Mesleki sertifika</option>
                </select>
              </div>

              {/* Üslup */}
              <div>
                <label className="block text-xs font-medium text-zinc-400 mb-1.5">Anlatım Üslubu</label>
                <div className="grid grid-cols-3 gap-2">
                  {([
                    { value: "Formal", label: "Akademik" },
                    { value: "Friendly", label: "Samimi" },
                    { value: "Playful", label: "Oyunsu" },
                  ] as { value: LearningTone; label: string }[]).map((opt) => (
                    <button
                      key={opt.value}
                      type="button"
                      onClick={() => patchLearning("learningTone", opt.value)}
                      className={`py-2.5 text-xs font-medium rounded-lg border transition-colors duration-150 ${
                        learning.learningTone === opt.value
                          ? "bg-zinc-100 text-zinc-950 border-transparent"
                          : "bg-zinc-900 border-zinc-800 text-zinc-400 hover:text-zinc-200"
                      }`}
                    >
                      {opt.label}
                    </button>
                  ))}
                </div>
              </div>

              {/* Günlük çalışma */}
              <div>
                <label className="block text-xs font-medium text-zinc-400 mb-1.5">Günlük çalışma süren</label>
                <div className="flex items-center gap-3">
                  <input
                    type="range"
                    min={5}
                    max={240}
                    step={5}
                    value={learning.dailyStudyMinutes}
                    onChange={(e) => patchLearning("dailyStudyMinutes", parseInt(e.target.value, 10))}
                    className="flex-1 accent-emerald-500"
                  />
                  <span className="w-14 text-right text-sm text-zinc-200 tabular-nums">
                    {learning.dailyStudyMinutes} dk
                  </span>
                </div>
              </div>

              {/* Save */}
              <button
                type="button"
                onClick={handleLearningSave}
                disabled={!learningDirty || saving}
                className={`w-full py-2.5 rounded-lg text-sm font-semibold transition-colors duration-150 ${
                  learningDirty && !saving
                    ? "bg-emerald-500/15 text-emerald-400 hover:bg-emerald-500/25 border border-emerald-500/30"
                    : "bg-zinc-900 text-zinc-600 border border-zinc-800 cursor-not-allowed"
                }`}
              >
                {saving ? "Kaydediliyor..." : learningDirty ? "Profili Kaydet" : "Değişiklik yok"}
              </button>
            </div>
          </SettingsSection>

          <div className="border-t soft-border mb-6" />

          {/* ── ACCOUNT & SECURITY ── */}
          <SettingsSection title={t("account_security") || "Hesap ve Güvenlik"} icon={<Shield className="w-4 h-4" />}>
            <div className="px-4 py-3 text-[12px] soft-text-muted leading-relaxed">
              Hesabınızı sildiğinizde tüm sohbet geçmişiniz, oluşturduğunuz planlar ve wiki sayfaları kalıcı olarak silinecektir. Bu işlem geri alınamaz.
            </div>
            <div className="px-4 pb-2">
              {!showConfirmDelete ? (
                <button
                  onClick={() => setShowConfirmDelete(true)}
                  className="w-full px-4 py-3 rounded-xl text-sm font-semibold text-amber-700 dark:text-amber-300 bg-amber-500/10 hover:bg-amber-500/15 border border-amber-500/25 transition-all duration-200 text-center"
                >
                  {t("delete_account") || "Hesabımı Kalıcı Sil"}
                </button>
              ) : (
                <div className="flex flex-col gap-3 p-4 rounded-xl border border-amber-500/30 bg-amber-500/10">
                   <p className="text-sm text-amber-700 dark:text-amber-300 text-center font-medium">Bu işlem geri alınamaz. Emin misiniz?</p>
                   <div className="flex gap-2">
                     <button
                        onClick={() => setShowConfirmDelete(false)}
                        className="flex-1 px-4 py-2 rounded-lg text-sm font-medium soft-text-muted soft-muted hover:text-foreground transition-colors"
                     >
                        İptal
                     </button>
                     <button
                        onClick={handleDeleteAccount}
                        id="confirm-delete-btn"
                        className="flex-1 px-4 py-2 rounded-lg text-sm font-medium text-amber-950 bg-amber-500 hover:bg-amber-400 transition-colors"
                     >
                        Evet, Sil
                     </button>
                   </div>
                </div>
              )}
            </div>
          </SettingsSection>

          {/* Footer */}
          <div className="mt-8 pt-6 border-t border-zinc-800/50 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <OrcaLogo className="w-4 h-4 text-zinc-600" />
              <span className="text-[11px] text-zinc-600">Orka AI v1.0</span>
            </div>
            <div className="flex gap-4">
              <button className="text-[11px] text-zinc-600 hover:text-zinc-400 transition-colors">
                Privacy Policy
              </button>
              <button className="text-[11px] text-zinc-600 hover:text-zinc-400 transition-colors">
                Terms of Service
              </button>
            </div>
          </div>

        </div>
      </div>
    </div>
  );
}
