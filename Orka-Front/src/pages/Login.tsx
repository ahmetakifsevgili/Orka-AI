/*
 * Design: "Sessiz Lüks" — Full-screen auth page.
 * Split layout: Left panel with branding, right panel with form.
 * Zinc monochrome, no neon, no glassmorphism.
 * Tabs for Sign In / Sign Up.
 */

import { useState } from "react";
import { useLocation } from "wouter";
import { motion } from "framer-motion";
import { Eye, EyeOff, ArrowRight, Mail, Lock, User, AlertCircle } from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";
import { AuthAPI, storage } from "../services/api";

type AuthTab = "signin" | "signup";

export default function Login() {
  const [tab, setTab] = useState<AuthTab>("signin");
  const [, navigate] = useLocation();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    // Basic Validation
    if (!email || !password) {
      setError("Lütfen tüm alanları doldurun.");
      return;
    }
    if (tab === "signup" && !name) {
      setError("Lütfen adınızı ve soyadınızı girin.");
      return;
    }

    setIsLoading(true);

    try {
      if (tab === "signin") {
        const { data } = await AuthAPI.login({ email, password });
        storage.save(data);
      } else {
        const parts = name.trim().split(/\s+/);
        const firstName = parts[0] || "Yeni";
        const lastName = parts.length > 1 ? parts.slice(1).join(" ") : "Kullanıcı";
        
        const { data } = await AuthAPI.register({ 
          firstName, 
          lastName, 
          email, 
          password 
        });
        storage.save(data);
      }
      navigate("/app");
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data
          ?.message ?? "Giriş başarısız. Lütfen bilgilerinizi kontrol edin.";
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="h-screen flex bg-zinc-950">
      {/* Left Branding Panel */}
      <div className="hidden lg:flex lg:w-[45%] relative overflow-hidden">
        <div className="absolute inset-0 bg-zinc-900" />
        {/* Subtle grid pattern */}
        <div
          className="absolute inset-0 opacity-[0.03]"
          style={{
            backgroundImage:
              "linear-gradient(rgba(255,255,255,0.1) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.1) 1px, transparent 1px)",
            backgroundSize: "60px 60px",
          }}
        />

        <div className="relative z-10 flex flex-col justify-between p-12 w-full">
          {/* Logo */}
          <div className="flex items-center gap-3">
            <OrcaLogo className="w-7 h-7 text-zinc-100" />
            <span className="text-lg font-semibold text-zinc-100 tracking-tight">
              Orka AI
            </span>
          </div>

          {/* Center content */}
          <div className="max-w-md">
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.6, delay: 0.2 }}
            >
              <h2 className="text-4xl font-bold text-zinc-100 leading-tight mb-6">
                Öğrenmenin geleceğine
                <br />
                <span className="text-zinc-500">hoş geldiniz.</span>
              </h2>
              <p className="text-base text-zinc-500 leading-relaxed max-w-sm">
                Yapay zeka destekli otonom müfredat, interaktif
                quizler ve canlı bilgi kütüphanesi ile her konuda uzmanlaşın.
              </p>
            </motion.div>

            {/* Feature highlights */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.6, delay: 0.4 }}
              className="mt-12 space-y-6"
            >
              {[
                {
                  title: "Akıllı Öğrenme Planı (DeepPlan)",
                  desc: "Seviyenize göre kişiselleştirilmiş müfredat oluşturur ve dallanma yapar.",
                },
                {
                  title: "Adaptif Değerlendirme",
                  desc: "Seviyenize göre anlık güncellenen akıllı soru bankası.",
                },
                {
                  title: "Öğrenme Haritası",
                  desc: "İlerlemeniz otonom bir ağaç yapısında anlık olarak görselleştirilir.",
                },
              ].map((feature) => (
                <div
                  key={feature.title}
                  className="flex items-start gap-4"
                >
                  <div className="w-1.5 h-1.5 rounded-full bg-zinc-500 mt-2.5 flex-shrink-0" />
                  <div>
                    <p className="text-base font-medium text-zinc-200">
                      {feature.title}
                    </p>
                    <p className="text-sm text-zinc-600 leading-snug">{feature.desc}</p>
                  </div>
                </div>
              ))}
            </motion.div>
          </div>

          {/* Bottom */}
          <p className="text-[10px] text-zinc-700">
            &copy; 2026 Orka AI. Tüm hakları saklıdır.
          </p>
        </div>
      </div>

      {/* Right Form Panel */}
      <div className="flex-1 flex items-center justify-center px-6">
        <motion.div
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5 }}
          className="w-full max-w-sm"
        >
          {/* Mobile logo */}
          <div className="flex items-center gap-3 mb-10 lg:hidden">
            <OrcaLogo className="w-6 h-6 text-zinc-100" />
            <span className="text-base font-semibold text-zinc-100">
              Orka AI
            </span>
          </div>

          {/* Tab Switcher */}
          <div className="flex gap-1 p-1 bg-zinc-900 rounded-lg mb-8">
            <button
              onClick={() => { setTab("signin"); setError(null); }}
              className={`flex-1 py-2.5 text-sm font-medium rounded-md transition-colors duration-150 ${
                tab === "signin"
                  ? "bg-zinc-800 text-zinc-100"
                  : "text-zinc-500 hover:text-zinc-400"
              }`}
            >
              Giriş Yap
            </button>
            <button
              onClick={() => { setTab("signup"); setError(null); }}
              className={`flex-1 py-2.5 text-sm font-medium rounded-md transition-colors duration-150 ${
                tab === "signup"
                  ? "bg-zinc-800 text-zinc-100"
                  : "text-zinc-500 hover:text-zinc-400"
              }`}
            >
              Üye Ol
            </button>
          </div>

          {/* Error banner */}
          {error && (
            <div className="flex items-start gap-2.5 p-3 bg-red-950/40 border border-red-900/50 rounded-lg mb-4">
              <AlertCircle className="w-4 h-4 text-red-400 mt-0.5 flex-shrink-0" />
              <p className="text-xs text-red-400">{error}</p>
            </div>
          )}

          {/* Form */}
          <form onSubmit={handleSubmit} className="space-y-4">
            {tab === "signup" && (
              <motion.div
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: "auto" }}
                exit={{ opacity: 0, height: 0 }}
                transition={{ duration: 0.2 }}
              >
                <label className="block text-xs font-medium text-zinc-500 mb-1.5">
                  Ad Soyad
                </label>
                <div className="relative">
                  <User className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-zinc-600" />
                  <input
                    type="text"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="Adınızı girin"
                    className="w-full pl-10 pr-4 py-3 bg-zinc-900 border border-zinc-800 rounded-lg text-sm text-zinc-100 placeholder-zinc-600 outline-none focus:border-zinc-700 transition-colors duration-150"
                  />
                </div>
              </motion.div>
            )}

            <div>
              <label className="block text-xs font-medium text-zinc-500 mb-1.5">
                E-posta
              </label>
              <div className="relative">
                <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-zinc-600" />
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="ornek@email.com"
                  className="w-full pl-10 pr-4 py-3 bg-zinc-900 border border-zinc-800 rounded-lg text-sm text-zinc-100 placeholder-zinc-600 outline-none focus:border-zinc-700 transition-colors duration-150"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-zinc-500 mb-1.5">
                Şifre
              </label>
              <div className="relative">
                <Lock className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-zinc-600" />
                <input
                  type={showPassword ? "text" : "password"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••"
                  className="w-full pl-10 pr-10 py-3 bg-zinc-900 border border-zinc-800 rounded-lg text-sm text-zinc-100 placeholder-zinc-600 outline-none focus:border-zinc-700 transition-colors duration-150"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-zinc-600 hover:text-zinc-400 transition-colors duration-150"
                >
                  {showPassword ? (
                    <EyeOff className="w-4 h-4" />
                  ) : (
                    <Eye className="w-4 h-4" />
                  )}
                </button>
              </div>
            </div>

            {tab === "signin" && (
              <div className="flex justify-end">
                <button
                  type="button"
                  className="text-xs text-zinc-500 hover:text-zinc-400 transition-colors duration-150"
                >
                  Şifremi unuttum
                </button>
              </div>
            )}

            <button
              type="submit"
              disabled={isLoading || !email || !password || (tab === "signup" && !name)}
              className="w-full flex items-center justify-center gap-2 py-3 bg-zinc-100 text-zinc-950 rounded-lg text-sm font-medium hover:bg-zinc-200 transition-colors duration-150 disabled:opacity-30 disabled:cursor-not-allowed"
            >
              {isLoading ? (
                <div className="w-4 h-4 border-2 border-zinc-400 border-t-zinc-950 rounded-full animate-spin" />
              ) : (
                <>
                  {tab === "signin" ? "Giriş Yap" : "Hesap Oluştur"}
                  <ArrowRight className="w-4 h-4" />
                </>
              )}
            </button>
          </form>

          {/* Divider */}
          <div className="flex items-center gap-3 my-6">
            <div className="flex-1 h-px bg-zinc-800" />
            <span className="text-[10px] text-zinc-600 uppercase tracking-wider">
              veya
            </span>
            <div className="flex-1 h-px bg-zinc-800" />
          </div>

          {/* Social Login */}
          <div className="space-y-2.5">
            <button
              type="button"
              disabled
              className="w-full flex items-center justify-center gap-2.5 py-3 bg-zinc-900 border border-zinc-800 rounded-lg text-sm text-zinc-600 cursor-not-allowed opacity-60"
            >
              <svg className="w-4 h-4" viewBox="0 0 24 24" fill="currentColor">
                <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 01-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z" />
                <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
                <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" />
                <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" />
              </svg>
              Google (Yakında)
            </button>
            <button
              type="button"
              disabled
              className="w-full flex items-center justify-center gap-2.5 py-3 bg-zinc-900 border border-zinc-800 rounded-lg text-sm text-zinc-600 cursor-not-allowed opacity-60"
            >
              <svg className="w-4 h-4" viewBox="0 0 24 24" fill="currentColor">
                <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z" />
              </svg>
              GitHub (Yakında)
            </button>
          </div>

          {/* Footer */}
          <p className="text-[10px] text-zinc-600 text-center mt-8">
            Devam ederek{" "}
            <button className="text-zinc-500 hover:text-zinc-400 underline underline-offset-2">
              Kullanım Koşulları
            </button>{" "}
            ve{" "}
            <button className="text-zinc-500 hover:text-zinc-400 underline underline-offset-2">
              Gizlilik Politikası
            </button>
            'nı kabul etmiş olursunuz.
          </p>
        </motion.div>
      </div>
    </div>
  );
}
