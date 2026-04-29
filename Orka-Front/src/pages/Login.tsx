import { useState } from "react";
import { useLocation, Link } from "wouter";
import { AnimatePresence, motion } from "framer-motion";
import { AlertCircle, ArrowRight, BrainCircuit, Eye, EyeOff, Lock, Mail, Sparkles, User } from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";
import { AuthAPI, storage } from "../services/api";

type AuthTab = "signin" | "signup";

type ApiError = {
  response?: {
    status?: number;
    data?: { message?: string; statusCode?: number };
    headers?: Record<string, string | undefined>;
  };
  message?: string;
};

function authErrorMessage(err: unknown, mode: AuthTab) {
  const apiError = err as ApiError;
  const status = apiError.response?.status;
  const serverMessage = apiError.response?.data?.message;
  const correlationId = apiError.response?.headers?.["x-correlation-id"];
  const suffix = correlationId ? ` Destek kodu: ${correlationId}` : "";

  if (!apiError.response) {
    return "Backend'e ulasilamiyor. API'nin http://localhost:5065 uzerinde calistigini ve Swagger'in acildigini kontrol et.";
  }

  if (status === 401) return "E-posta veya sifre hatali. Bilgileri kontrol edip tekrar dene.";
  if (status === 404) return "Bu e-posta ile kayitli kullanici bulunamadi.";
  if (status === 400) return `${serverMessage ?? (mode === "signup" ? "Uyelik bilgilerini kontrol et." : "Giris bilgilerini kontrol et.")}${suffix}`;
  if (status && status >= 500) return `Sunucu tarafinda bir sorun var. Health/Swagger durumunu kontrol edecegiz.${suffix}`;
  return `${serverMessage ?? "Islem tamamlanamadi. Lutfen tekrar dene."}${suffix}`;
}

const previewRows = [
  ["Dinamik Müfredat", "Zihinsel modelinize %100 uyumlu, adaptif öğrenme yolları"],
  ["Derin Analitik", "Beceri boşluklarınızı tespit eden gerçek zamanlı teşhis motoru"],
  ["Bilgi Hafızası", "Öğrendiğiniz her konuyu indeksleyen otonom Wiki sistemi"],
  ["Ajan Ağı", "Kişisel öğrenme hızınıza senkronize olan yapay zeka asistanları"],
];

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

    if (!email || !password) {
      setError("Lutfen e-posta ve sifre alanlarini doldur.");
      return;
    }
    if (tab === "signup" && !name.trim()) {
      setError("Uyelik icin ad soyad bilgisi gerekiyor.");
      return;
    }

    setIsLoading(true);
    try {
      if (tab === "signin") {
        const { data } = await AuthAPI.login({ email, password });
        storage.save(data);
      } else {
        const [firstName = "Yeni", ...lastParts] = name.trim().split(/\s+/);
        const lastName = lastParts.join(" ");
        const { data } = await AuthAPI.register({ firstName, lastName, email, password });
        storage.save(data);
      }
      navigate("/app");
    } catch (err: unknown) {
      setError(authErrorMessage(err, tab));
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="min-h-screen orka-bg text-[#172033]">
      <div className="pointer-events-none fixed inset-0 mist-grid opacity-60" />
      <div className="relative z-10 mx-auto grid min-h-screen max-w-6xl gap-8 px-5 py-8 lg:grid-cols-[1.02fr_0.98fr] lg:items-center">
        <motion.section
          initial={{ opacity: 0, x: -26 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.65, ease: "easeOut" }}
          className="hidden lg:block"
        >
          <Link href="/" className="mb-10 inline-flex items-center gap-3">
            <span className="grid h-11 w-11 place-items-center rounded-2xl bg-[#172033] text-white shadow-lg shadow-slate-900/10">
              <OrcaLogo className="h-6 w-6" />
            </span>
            <span className="text-base font-extrabold tracking-tight">Orka AI</span>
          </Link>

          <div className="max-w-xl">
            <div className="mb-5 inline-flex items-center gap-2 rounded-full border border-[#9ec7d9]/45 bg-white/62 px-3 py-1.5 text-xs font-extrabold text-[#2d5870] backdrop-blur">
              <Sparkles className="h-3.5 w-3.5" />
              Gelişmiş Bilişsel Mimari
            </div>
            <h1 className="font-display text-5xl font-bold leading-[1.05] text-[#172033] tracking-tight">
              Öğrenme potansiyelinizi <br/>yapay zeka ile ölçeklendirin.
            </h1>
            <p className="mt-6 text-[15px] leading-relaxed text-[#667085] max-w-md">
              Orka AI, çalışma stilinizi analiz eder ve tamamen size özel uyarlanmış, interaktif bir bilgi ekosistemi yaratır. Geleneksel öğrenmeyi geride bırakın.
            </p>
          </div>

          <div className="orka-glass mt-10 max-w-xl rounded-[2rem] p-5">
            <div className="mb-4 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <BrainCircuit className="h-5 w-5 text-[#52768a]" />
                <span className="text-sm font-extrabold text-[#172033]">Aktif Sistem Modülleri</span>
              </div>
              <span className="rounded-full bg-[#ddebe3] px-3 py-1 text-[10px] font-black uppercase tracking-[0.18em] text-[#47725d]">Senkronize</span>
            </div>
            <div className="space-y-3">
              {previewRows.map(([label, text], index) => (
                <motion.div
                  key={label}
                  initial={{ opacity: 0, y: 12 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: 0.25 + index * 0.08 }}
                  className="flex items-start gap-3 rounded-3xl bg-white/68 p-4 border border-[#526d82]/5"
                >
                  <span className="grid h-9 w-9 place-items-center rounded-2xl bg-[#dcecf3] text-xs font-black text-[#2d5870]">{index + 1}</span>
                  <div>
                    <p className="text-xs font-black uppercase tracking-[0.18em] text-[#52768a]">{label}</p>
                    <p className="mt-1 text-sm font-medium text-[#344054]">{text}</p>
                  </div>
                </motion.div>
              ))}
            </div>
          </div>
        </motion.section>

        <motion.section
          initial={{ opacity: 0, y: 24 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.58, ease: "easeOut" }}
          className="flex min-h-[calc(100vh-4rem)] items-center justify-center lg:min-h-0"
        >
          <div className="orka-glass w-full max-w-md rounded-[2rem] p-5 sm:p-7">
            <div className="mb-8 flex items-center justify-between lg:hidden">
              <Link href="/" className="flex items-center gap-3">
                <span className="grid h-10 w-10 place-items-center rounded-2xl bg-[#172033] text-white">
                  <OrcaLogo className="h-5 w-5" />
                </span>
                <span className="font-extrabold">Orka AI</span>
              </Link>
            </div>

            <div className="mb-7">
              <p className="text-xs font-extrabold uppercase tracking-[0.24em] text-[#52768a]">
                {tab === "signin" ? "Tekrar hos geldin" : "Yeni calisma alani"}
              </p>
              <h2 className="font-display mt-2 text-4xl font-bold text-[#172033]">
                {tab === "signin" ? "Giris yap" : "Hesap olustur"}
              </h2>
            </div>

            <div className="relative mb-6 grid grid-cols-2 rounded-full bg-white/62 p-1 shadow-inner shadow-slate-200/50">
              <motion.div
                layout
                className={`absolute bottom-1 top-1 w-[calc(50%-0.25rem)] rounded-full bg-[#172033] shadow-lg transition ${tab === "signup" ? "left-[calc(50%+0.125rem)]" : "left-1"}`}
              />
              <button
                type="button"
                onClick={() => { setTab("signin"); setError(null); }}
                className={`relative z-10 rounded-full py-2.5 text-sm font-extrabold transition ${tab === "signin" ? "text-white" : "text-[#667085]"}`}
              >
                Giris
              </button>
              <button
                type="button"
                onClick={() => { setTab("signup"); setError(null); }}
                className={`relative z-10 rounded-full py-2.5 text-sm font-extrabold transition ${tab === "signup" ? "text-white" : "text-[#667085]"}`}
              >
                Uye Ol
              </button>
            </div>

            <AnimatePresence>
              {error && (
                <motion.div
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -8 }}
                  className="mb-4 flex items-start gap-3 rounded-2xl border border-[#f2b8b5]/50 bg-[#fff1f0]/85 p-3 text-[#8a3f3a]"
                >
                  <AlertCircle className="mt-0.5 h-4 w-4 flex-shrink-0" />
                  <p className="text-xs leading-5">{error}</p>
                </motion.div>
              )}
            </AnimatePresence>

            <form onSubmit={handleSubmit} className="space-y-4">
              <AnimatePresence initial={false}>
                {tab === "signup" && (
                  <motion.div
                    initial={{ opacity: 0, height: 0 }}
                    animate={{ opacity: 1, height: "auto" }}
                    exit={{ opacity: 0, height: 0 }}
                    transition={{ duration: 0.2 }}
                    className="overflow-hidden"
                  >
                    <label className="mb-1.5 block text-xs font-bold text-[#667085]">Ad Soyad</label>
                    <div className="relative">
                      <User className="absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-[#98a2b3]" />
                      <input
                        type="text"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        placeholder="Adini yaz"
                        className="w-full rounded-2xl border border-[#526d82]/14 bg-white/70 py-3 pl-11 pr-4 text-sm text-[#172033] outline-none transition placeholder:text-[#98a2b3] focus:border-[#9ec7d9] focus:bg-white"
                      />
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>

              <div>
                <label className="mb-1.5 block text-xs font-bold text-[#667085]">E-posta</label>
                <div className="relative">
                  <Mail className="absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-[#98a2b3]" />
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="ornek@email.com"
                    className="w-full rounded-2xl border border-[#526d82]/14 bg-white/70 py-3 pl-11 pr-4 text-sm text-[#172033] outline-none transition placeholder:text-[#98a2b3] focus:border-[#9ec7d9] focus:bg-white"
                  />
                </div>
              </div>

              <div>
                <label className="mb-1.5 block text-xs font-bold text-[#667085]">Sifre</label>
                <div className="relative">
                  <Lock className="absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-[#98a2b3]" />
                  <input
                    type={showPassword ? "text" : "password"}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="••••••••"
                    className="w-full rounded-2xl border border-[#526d82]/14 bg-white/70 py-3 pl-11 pr-12 text-sm text-[#172033] outline-none transition placeholder:text-[#98a2b3] focus:border-[#9ec7d9] focus:bg-white"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    className="absolute right-4 top-1/2 -translate-y-1/2 text-[#98a2b3] transition hover:text-[#172033]"
                    aria-label={showPassword ? "Sifreyi gizle" : "Sifreyi goster"}
                  >
                    {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                  </button>
                </div>
              </div>

              <button
                type="submit"
                disabled={isLoading || !email || !password || (tab === "signup" && !name.trim())}
                className="orka-button mt-2 flex w-full items-center justify-center gap-2 rounded-2xl py-3 text-sm font-extrabold disabled:cursor-not-allowed disabled:opacity-45"
              >
                {isLoading ? (
                  <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                ) : (
                  <>
                    {tab === "signin" ? "Giris Yap" : "Hesap Olustur"}
                    <ArrowRight className="h-4 w-4" />
                  </>
                )}
              </button>
            </form>

            <p className="mt-6 text-center text-[11px] leading-5 text-[#667085]">
              Devam ederek Orka'nin kullanim ve gizlilik kosullarini kabul etmis olursun.
            </p>
          </div>
        </motion.section>
      </div>
    </div>
  );
}
