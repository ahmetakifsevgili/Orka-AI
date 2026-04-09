/*
 * Design: Clean settings panel with grouped sections.
 * Profile, Notifications, Appearance, Language, Account.
 * Toggle switches, dropdowns, clean form elements.
 */

import { useState } from "react";
import {
  User,
  Bell,
  Palette,
  Globe,
  Shield,
  ChevronRight,
  Moon,
  Sun,
  Monitor,
} from "lucide-react";
import OrcaLogo from "./OrcaLogo";

interface SettingsSectionProps {
  title: string;
  icon: React.ReactNode;
  children: React.ReactNode;
}

function SettingsSection({ title, icon, children }: SettingsSectionProps) {
  return (
    <div className="mb-6">
      <div className="flex items-center gap-2.5 mb-4">
        <span className="text-zinc-400">{icon}</span>
        <h3 className="text-sm font-semibold text-zinc-200">{title}</h3>
      </div>
      <div className="space-y-1">{children}</div>
    </div>
  );
}

interface ToggleRowProps {
  label: string;
  description?: string;
  checked: boolean;
  onChange: (val: boolean) => void;
}

function ToggleRow({ label, description, checked, onChange }: ToggleRowProps) {
  return (
    <div className="flex items-center justify-between px-4 py-3 rounded-lg hover:bg-zinc-800/30 transition-colors duration-150">
      <div>
        <p className="text-sm text-zinc-300">{label}</p>
        {description && (
          <p className="text-[11px] text-zinc-500 mt-0.5">{description}</p>
        )}
      </div>
      <button
        onClick={() => onChange(!checked)}
        className={`w-9 h-5 rounded-full transition-colors duration-200 relative flex-shrink-0 ${
          checked ? "bg-zinc-500" : "bg-zinc-700"
        }`}
      >
        <div
          className={`w-4 h-4 rounded-full bg-zinc-100 absolute top-0.5 transition-transform duration-200 ${
            checked ? "translate-x-4.5" : "translate-x-0.5"
          }`}
        />
      </button>
    </div>
  );
}

interface SelectRowProps {
  label: string;
  value: string;
  options: string[];
  onChange: (val: string) => void;
}

function SelectRow({ label, value, options, onChange }: SelectRowProps) {
  return (
    <div className="flex items-center justify-between px-4 py-3 rounded-lg hover:bg-zinc-800/30 transition-colors duration-150">
      <p className="text-sm text-zinc-300">{label}</p>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-1.5 text-xs text-zinc-300 outline-none focus:border-zinc-600 transition-colors"
      >
        {options.map((opt) => (
          <option key={opt} value={opt}>
            {opt}
          </option>
        ))}
      </select>
    </div>
  );
}

function NavRow({ label, onClick }: { label: string; onClick?: () => void }) {
  return (
    <button
      onClick={onClick}
      className="flex items-center justify-between w-full px-4 py-3 rounded-lg hover:bg-zinc-800/30 transition-colors duration-150 text-left"
    >
      <p className="text-sm text-zinc-300">{label}</p>
      <ChevronRight className="w-4 h-4 text-zinc-600" />
    </button>
  );
}

export default function SettingsPanel() {
  const [notifications, setNotifications] = useState({
    quizReminders: true,
    weeklyReport: true,
    newContent: false,
    sounds: true,
  });
  const [theme, setTheme] = useState("Dark");
  const [language, setLanguage] = useState("English");
  const [fontSize, setFontSize] = useState("Medium");

  return (
    <div className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden">
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-2xl mx-auto w-full px-6 py-8">
          {/* Header */}
          <div className="mb-8">
            <h1 className="text-xl font-bold text-zinc-100 mb-1">Settings</h1>
            <p className="text-sm text-zinc-500">
              Manage your preferences and account settings
            </p>
          </div>

          {/* Profile Section */}
          <SettingsSection title="Profile" icon={<User className="w-4 h-4" />}>
            <div className="px-4 py-4 rounded-lg bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-4">
                <div className="w-14 h-14 rounded-full bg-zinc-800 border border-zinc-700 flex items-center justify-center">
                  <span className="text-lg font-semibold text-zinc-300">OK</span>
                </div>
                <div className="flex-1">
                  <p className="text-sm font-medium text-zinc-200">Orka User</p>
                  <p className="text-xs text-zinc-500 mt-0.5">orka@example.com</p>
                </div>
                <button className="px-3 py-1.5 rounded-lg border border-zinc-700 text-xs text-zinc-400 hover:text-zinc-200 hover:border-zinc-600 transition-colors duration-150">
                  Edit
                </button>
              </div>
            </div>
            <NavRow label="Change display name" />
            <NavRow label="Update email address" />
            <NavRow label="Change password" />
          </SettingsSection>

          <div className="border-t border-zinc-800/50 mb-6" />

          {/* Notifications */}
          <SettingsSection
            title="Notifications"
            icon={<Bell className="w-4 h-4" />}
          >
            <ToggleRow
              label="Quiz reminders"
              description="Get reminded to practice daily"
              checked={notifications.quizReminders}
              onChange={(val) =>
                setNotifications((prev) => ({ ...prev, quizReminders: val }))
              }
            />
            <ToggleRow
              label="Weekly progress report"
              description="Receive a summary of your learning progress"
              checked={notifications.weeklyReport}
              onChange={(val) =>
                setNotifications((prev) => ({ ...prev, weeklyReport: val }))
              }
            />
            <ToggleRow
              label="New content alerts"
              description="Notify when new wiki content is generated"
              checked={notifications.newContent}
              onChange={(val) =>
                setNotifications((prev) => ({ ...prev, newContent: val }))
              }
            />
            <ToggleRow
              label="Sound effects"
              description="Play sounds for quiz answers and notifications"
              checked={notifications.sounds}
              onChange={(val) =>
                setNotifications((prev) => ({ ...prev, sounds: val }))
              }
            />
          </SettingsSection>

          <div className="border-t border-zinc-800/50 mb-6" />

          {/* Appearance */}
          <SettingsSection
            title="Appearance"
            icon={<Palette className="w-4 h-4" />}
          >
            {/* Theme Selector */}
            <div className="px-4 py-3">
              <p className="text-sm text-zinc-300 mb-3">Theme</p>
              <div className="flex gap-2">
                {[
                  { label: "Light", icon: Sun },
                  { label: "Dark", icon: Moon },
                  { label: "System", icon: Monitor },
                ].map((t) => (
                  <button
                    key={t.label}
                    onClick={() => setTheme(t.label)}
                    className={`flex items-center gap-2 px-4 py-2.5 rounded-lg border text-xs transition-all duration-150 ${
                      theme === t.label
                        ? "border-zinc-600 bg-zinc-800 text-zinc-100"
                        : "border-zinc-800 text-zinc-500 hover:border-zinc-700 hover:text-zinc-300"
                    }`}
                  >
                    <t.icon className="w-3.5 h-3.5" />
                    {t.label}
                  </button>
                ))}
              </div>
            </div>
            <SelectRow
              label="Font size"
              value={fontSize}
              options={["Small", "Medium", "Large"]}
              onChange={setFontSize}
            />
          </SettingsSection>

          <div className="border-t border-zinc-800/50 mb-6" />

          {/* Language */}
          <SettingsSection
            title="Language & Region"
            icon={<Globe className="w-4 h-4" />}
          >
            <SelectRow
              label="Interface language"
              value={language}
              options={["English", "Türkçe", "Deutsch", "Français", "日本語"]}
              onChange={setLanguage}
            />
            <SelectRow
              label="Content language"
              value="English"
              options={["English", "Türkçe", "Multi-language"]}
              onChange={() => {}}
            />
          </SettingsSection>

          <div className="border-t border-zinc-800/50 mb-6" />

          {/* Account & Security */}
          <SettingsSection
            title="Account & Security"
            icon={<Shield className="w-4 h-4" />}
          >
            <NavRow label="Two-factor authentication" />
            <NavRow label="Connected accounts" />
            <NavRow label="Export my data" />
            <div className="mt-2">
              <button className="px-4 py-2.5 rounded-lg text-xs text-red-400 hover:text-red-300 hover:bg-red-900/10 transition-colors duration-150">
                Delete account
              </button>
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
