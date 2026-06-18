import { useState } from "react";
import { useLocation, Link } from "wouter";
import { AnimatePresence, motion } from "framer-motion";
import { AlertCircle, ArrowRight, BookOpenCheck, ClipboardList, Eye, EyeOff, Lock, Mail, ShieldCheck, User } from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";
import { API_ORIGIN } from "../services/api";
import { useAuth } from "../contexts/AuthContext";

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
    const base = API_ORIGIN || "http://localhost:5065";
    return `Backend'e ulaşılamıyor. API'nin ${base} üzerinde çalıştığını ve Swagger'ın açıldığını kontrol et.`;
  }

  if (status === 401) return "E-posta veya şifre hatalı. Bilgileri kontrol edip tekrar dene.";
  if (status === 404) return "Bu e-posta ile kayıtlı kullanıcı bulunamadı.";
  if (status === 400) return `${serverMessage ?? (mode === "signup" ? "Üyelik bilgilerini kontrol et." : "Giriş bilgilerini kontrol et.")}${suffix}`;
  if (status && status >= 500) return `Sunucu tarafında bir sorun var. Health/Swagger durumunu kontrol edeceğiz.${suffix}`;
  return `${serverMessage ?? "İşlem tamamlanamadı. Lütfen tekrar dene."}${suffix}`;
}

const previewRows = [
  ["Ders başlangıcı", "Bugünkü hedef kısa bir ders akışına çevrilir"],
  ["Kaynak bağlantısı", "Not, PDF veya yanlış listesi plana dayanak olur"],
  ["Mini kontrol", "Dersin sonunda küçük bir anlama kontrolü gelir"],
  ["Telafi adımı", "Takıldığın yer bir sonraki güvenli adıma dönüşür"],
];

export default function Login() {
  const [tab, setTab] = useState<AuthTab>("signin");
  const [, navigate] = useLocation();
  const { login, register } = useAuth();
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
      setError("Lütfen e-posta ve şifre alanlarını doldur.");
      return;
    }
    if (tab === "signup" && !name.trim()) {
      setError("Üyelik için ad soyad bilgisi gerekiyor.");
      return;
    }

    setIsLoading(true);
    try {
      if (tab === "signin") {
        await login({ email, password });
      } else {
        const [firstName = "Yeni", ...lastParts] = name.trim().split(/\s+/);
        const lastName = lastParts.join(" ");
        await register({ firstName, lastName, email, password });
      }
      navigate("/app");
    } catch (err: unknown) {
      setError(authErrorMessage(err, tab));
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-[#f7f8f6] text-[#101418]">
      <div className="pointer-events-none fixed inset-0 bg-[linear-gradient(180deg,#fbfcfb_0%,#f1f4f2_100%)]" />
      <div className="pointer-events-none fixed inset-x-0 top-0 h-72 border-b border-[#e0e7e3] bg-white/60" />
      <div className="relative z-10 mx-auto grid min-h-screen max-w-6xl gap-8 px-5 py-8 lg:grid-cols-[1.02fr_0.98fr] lg:items-center">
        <motion.section
          initial={{ opacity: 0, x: -26 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.65, ease: "easeOut" }}
          className="hidden lg:block"
        >
          <Link href="/" className="mb-10 inline-flex items-center gap-3">
            <span className="grid h-10 w-10 place-items-center rounded-lg bg-[#101418] text-white shadow-[0_12px_30px_rgba(16,20,24,0.12)]">
              <OrcaLogo className="h-6 w-6" />
            </span>
            <span className="text-base font-black tracking-tight">Orka</span>
          </Link>

          <div className="max-w-xl">
            <div className="mb-5 inline-flex items-center gap-2 rounded-md border border-[#d9e1dd] bg-white px-3 py-1.5 text-xs font-black text-[#35564d] shadow-[0_8px_24px_rgba(28,40,38,0.06)]">
              <BookOpenCheck className="h-3.5 w-3.5" />
              Ders çalışma alanı
            </div>
            <h1 className="text-5xl font-black leading-[1.03] text-[#101418] tracking-tight">
              Bugünkü dersini aç, sonra küçük adımlarla ilerle.
            </h1>
            <p className="mt-6 max-w-md text-[15px] leading-7 text-[#65726d]">
              Girişten sonra Orka hedefini, kaynaklarını ve son hatalarını okuyup sana sade bir ders akışı hazırlar.
            </p>
          </div>

          <div className="mt-10 max-w-xl rounded-2xl border border-[#d9e1dd] bg-white p-5 shadow-[0_18px_55px_rgba(28,40,38,0.08)]">
            <div className="mb-4 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <ClipboardList className="h-5 w-5 text-[#52768a]" />
                <span className="text-sm font-black text-[#101418]">Ders başlamadan önce</span>
              </div>
              <span className="rounded-md bg-[#eef5f1] px-3 py-1 text-[10px] font-black uppercase tracking-[0.14em] text-[#47725d]">Hazır</span>
            </div>
            <div className="space-y-3">
              {previewRows.map(([label, text], index) => (
                <motion.div
                  key={label}
                  initial={{ opacity: 0, y: 12 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: 0.25 + index * 0.08 }}
                  className="flex items-start gap-3 rounded-xl border border-[#e3e9e6] bg-[#fbfcfb] p-4"
                >
                  <span className="grid h-8 w-8 place-items-center rounded-md bg-[#edf4f6] text-xs font-black text-[#2d5870]">{index + 1}</span>
                  <div>
                    <p className="text-xs font-black uppercase tracking-[0.14em] text-[#52768a]">{label}</p>
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
          <div className="w-full max-w-md rounded-2xl border border-[#d9e1dd] bg-white p-5 shadow-[0_22px_70px_rgba(28,40,38,0.1)] sm:p-7">
            <div className="mb-8 flex items-center justify-between lg:hidden">
              <Link href="/" className="flex items-center gap-3">
                <span className="grid h-10 w-10 place-items-center rounded-lg bg-[#101418] text-white">
                  <OrcaLogo className="h-5 w-5" />
                </span>
                <span className="font-black">Orka</span>
              </Link>
            </div>

            <div className="mb-7">
              <p className="text-xs font-black uppercase tracking-[0.18em] text-[#52768a]">
                {tab === "signin" ? "Ders alanına dön" : "Yeni çalışma alanı"}
              </p>
              <h2 className="mt-2 text-4xl font-black text-[#101418]">
                {tab === "signin" ? "Giriş yap" : "Hesap oluştur"}
              </h2>
            </div>

            <div className="relative mb-6 grid grid-cols-2 rounded-lg border border-[#d9e1dd] bg-[#f6f8f7] p-1">
              <motion.div
                layout
                className={`absolute bottom-1 top-1 w-[calc(50%-0.25rem)] rounded-md bg-[#101418] shadow-[0_8px_22px_rgba(16,20,24,0.16)] transition ${tab === "signup" ? "left-[calc(50%+0.125rem)]" : "left-1"}`}
              />
              <button
                type="button"
                onClick={() => { setTab("signin"); setError(null); }}
                className={`relative z-10 rounded-md py-2.5 text-sm font-black transition ${tab === "signin" ? "text-white" : "text-[#667085]"}`}
              >
                Giriş
              </button>
              <button
                type="button"
                onClick={() => { setTab("signup"); setError(null); }}
                className={`relative z-10 rounded-md py-2.5 text-sm font-black transition ${tab === "signup" ? "text-white" : "text-[#667085]"}`}
              >
                Üye ol
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
                    <label className="mb-1.5 block text-xs font-bold text-[#667085]">Ad soyad</label>
                    <div className="relative">
                      <User className="absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-[#98a2b3]" />
                      <input
                        type="text"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        placeholder="Adını yaz"
                        className="w-full rounded-xl border border-[#d9e1dd] bg-[#fbfcfb] py-3 pl-11 pr-4 text-sm text-[#101418] outline-none transition placeholder:text-[#98a2b3] focus:border-[#8bb9aa] focus:bg-white focus:ring-4 focus:ring-[#8bb9aa]/15"
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
                    className="w-full rounded-xl border border-[#d9e1dd] bg-[#fbfcfb] py-3 pl-11 pr-4 text-sm text-[#101418] outline-none transition placeholder:text-[#98a2b3] focus:border-[#8bb9aa] focus:bg-white focus:ring-4 focus:ring-[#8bb9aa]/15"
                  />
                </div>
              </div>

              <div>
                <label className="mb-1.5 block text-xs font-bold text-[#667085]">Şifre</label>
                <div className="relative">
                  <Lock className="absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-[#98a2b3]" />
                  <input
                    type={showPassword ? "text" : "password"}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="••••••••"
                    className="w-full rounded-xl border border-[#d9e1dd] bg-[#fbfcfb] py-3 pl-11 pr-12 text-sm text-[#101418] outline-none transition placeholder:text-[#98a2b3] focus:border-[#8bb9aa] focus:bg-white focus:ring-4 focus:ring-[#8bb9aa]/15"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    className="absolute right-4 top-1/2 -translate-y-1/2 text-[#98a2b3] transition hover:text-[#172033]"
                    aria-label={showPassword ? "Şifreyi gizle" : "Şifreyi göster"}
                  >
                    {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                  </button>
                </div>
              </div>

              <button
                type="submit"
                disabled={isLoading || !email || !password || (tab === "signup" && !name.trim())}
                className="mt-2 flex w-full items-center justify-center gap-2 rounded-xl border border-[#101418] bg-[#101418] py-3 text-sm font-black text-white shadow-[0_14px_34px_rgba(16,20,24,0.16)] transition hover:bg-[#26313b] focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-[#8bb9aa]/25 disabled:cursor-not-allowed disabled:opacity-45"
              >
                {isLoading ? (
                  <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                ) : (
                  <>
                    {tab === "signin" ? "Giriş yap" : "Hesap oluştur"}
                    <ArrowRight className="h-4 w-4" />
                  </>
                )}
              </button>
            </form>

            <p className="mt-6 flex items-start justify-center gap-2 text-center text-[11px] leading-5 text-[#667085]">
              <ShieldCheck className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[#668a7f]" />
              <span>Devam ederek Orka'nın kullanım ve gizlilik koşullarını kabul etmiş olursun.</span>
            </p>
          </div>
        </motion.section>
      </div>
    </div>
  );
}
