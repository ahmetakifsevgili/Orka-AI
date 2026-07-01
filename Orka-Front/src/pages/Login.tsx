import { useState } from "react";
import { useLocation, Link } from "wouter";
import { AnimatePresence, motion } from "framer-motion";
import { AlertCircle, ArrowRight, Eye, EyeOff, Lock, Mail, ShieldCheck, User } from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";
import { API_ORIGIN, AuthAPI, storage } from "../services/api";

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
    return `Backend'e ulasilamiyor. API'nin ${base} uzerinde calistigini kontrol et.`;
  }

  if (status === 401) return "E-posta veya sifre hatali. Bilgileri kontrol edip tekrar dene.";
  if (status === 404) return "Bu e-posta ile kayitli kullanici bulunamadi.";
  if (status === 400) return `${serverMessage ?? (mode === "signup" ? "Uyelik bilgilerini kontrol et." : "Giris bilgilerini kontrol et.")}${suffix}`;
  if (status && status >= 500) return `Sunucu tarafinda bir sorun var.${suffix}`;
  return `${serverMessage ?? "Islem tamamlanamadi. Lutfen tekrar dene."}${suffix}`;
}

/* ─── Left panel decorative feature list ─────────────────────────────────── */
const FEATURES = [
  { label: "Kisisel Wiki", desc: "Sohbetlerinden otomatik notlar ve ozet sayfasi olusur." },
  { label: "Planlama Ajani", desc: "Hedefini soyle; yapi ve musfredat sana gelir." },
  { label: "Mini Quizler", desc: "Her dersin sonunda aninda bilgi kontrolu yapilir." },
  { label: "Kaynak Ajani", desc: "PDF ve belgelerine ozgu ajan aninda cevap verir." },
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
    <div
      className="min-h-screen font-sans"
      style={{ background: "#09090b", color: "#fafafa" }}
    >
      {/* Subtle ambient glow top */}
      <div
        className="pointer-events-none fixed left-1/2 top-0 -translate-x-1/2 h-[400px] w-[800px] opacity-[0.06] rounded-full"
        style={{
          background: "radial-gradient(ellipse at center, #6ed7ce 0%, transparent 65%)",
          filter: "blur(48px)",
        }}
      />

      <div className="relative z-10 mx-auto grid min-h-screen max-w-6xl gap-12 px-6 py-10 lg:grid-cols-2 lg:items-center">

        {/* ── Left panel ────────────────────────────────────────────────── */}
        <motion.div
          initial={{ opacity: 0, x: -20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.55, ease: "easeOut" }}
          className="hidden lg:flex flex-col"
        >
          {/* Logo */}
          <Link href="/" className="mb-12 inline-flex items-center gap-2.5">
            <span className="grid h-8 w-8 place-items-center rounded-lg" style={{ background: "#6ed7ce" }}>
              <OrcaLogo className="h-4.5 w-4.5" style={{ color: "#041210" }} />
            </span>
            <span className="text-[16px] font-bold" style={{ color: "#fafafa" }}>Orka</span>
          </Link>

          <div className="max-w-md">
            <h1 className="text-[40px] font-bold leading-[1.08] tracking-tight text-balance mb-5" style={{ color: "#fafafa" }}>
              Ogrenmen icin kisisel bir AI ajani.
            </h1>
            <p className="text-[15px] leading-relaxed mb-10" style={{ color: "#71717a" }}>
              Giristen sonra Orka hedefini okur, kaynaklarini analiz eder ve sana ozel bir ders akisi hazirlar.
            </p>
          </div>

          <div className="space-y-3">
            {FEATURES.map((f, i) => (
              <motion.div
                key={f.label}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.18 + i * 0.07, ease: "easeOut", duration: 0.45 }}
                className="flex items-start gap-3 rounded-xl p-4"
                style={{
                  background: "rgba(255,255,255,0.025)",
                  border: "1px solid rgba(255,255,255,0.06)",
                }}
              >
                <span
                  className="mt-0.5 grid h-6 w-6 flex-none place-items-center rounded-md text-[11px] font-bold"
                  style={{ background: "rgba(110,215,206,0.08)", color: "#6ed7ce" }}
                >
                  {i + 1}
                </span>
                <div>
                  <p className="text-[13px] font-semibold mb-0.5" style={{ color: "#e4e4e7" }}>{f.label}</p>
                  <p className="text-[12px] leading-5" style={{ color: "#52525b" }}>{f.desc}</p>
                </div>
              </motion.div>
            ))}
          </div>
        </motion.div>

        {/* ── Right panel — form ─────────────────────────────────────────── */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.52, ease: "easeOut" }}
          className="flex min-h-[calc(100vh-5rem)] items-center justify-center lg:min-h-0"
        >
          <div
            className="w-full max-w-md rounded-2xl p-7"
            style={{
              background: "rgba(17,17,19,0.95)",
              border: "1px solid rgba(255,255,255,0.08)",
              boxShadow: "0 24px 64px rgba(0,0,0,0.56)",
            }}
          >
            {/* Mobile logo */}
            <div className="mb-7 flex items-center justify-between lg:hidden">
              <Link href="/" className="flex items-center gap-2">
                <span className="grid h-7 w-7 place-items-center rounded-md" style={{ background: "#6ed7ce" }}>
                  <OrcaLogo className="h-4 w-4" style={{ color: "#041210" }} />
                </span>
                <span className="text-[15px] font-bold" style={{ color: "#fafafa" }}>Orka</span>
              </Link>
            </div>

            {/* Heading */}
            <div className="mb-6">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] mb-2" style={{ color: "#52525b" }}>
                {tab === "signin" ? "Hesabina geri don" : "Yeni calisma alani"}
              </p>
              <h2 className="text-[28px] font-bold" style={{ color: "#fafafa" }}>
                {tab === "signin" ? "Giris yap" : "Hesap olustur"}
              </h2>
            </div>

            {/* Tab switcher */}
            <div
              className="relative mb-6 grid grid-cols-2 rounded-lg p-0.5"
              style={{ background: "rgba(255,255,255,0.04)", border: "1px solid rgba(255,255,255,0.07)" }}
            >
              <motion.div
                layout
                transition={{ type: "spring", stiffness: 400, damping: 32 }}
                className="absolute inset-y-0.5 w-[calc(50%-2px)] rounded-md"
                style={{
                  left: tab === "signin" ? "2px" : "calc(50%)",
                  background: "rgba(255,255,255,0.08)",
                  border: "1px solid rgba(255,255,255,0.1)",
                }}
              />
              {(["signin", "signup"] as AuthTab[]).map((t) => (
                <button
                  key={t}
                  type="button"
                  onClick={() => { setTab(t); setError(null); }}
                  className="relative z-10 rounded-md py-2 text-[13px] font-medium transition-colors"
                  style={{ color: tab === t ? "#fafafa" : "#52525b" }}
                >
                  {t === "signin" ? "Giris" : "Uye ol"}
                </button>
              ))}
            </div>

            {/* Error */}
            <AnimatePresence>
              {error && (
                <motion.div
                  initial={{ opacity: 0, y: -6 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -6 }}
                  className="mb-4 flex items-start gap-2.5 rounded-xl p-3"
                  style={{
                    background: "rgba(248,113,113,0.07)",
                    border: "1px solid rgba(248,113,113,0.18)",
                  }}
                >
                  <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" style={{ color: "#f87171" }} />
                  <p className="text-[12px] leading-5" style={{ color: "#f87171" }}>{error}</p>
                </motion.div>
              )}
            </AnimatePresence>

            {/* Form */}
            <form onSubmit={handleSubmit} className="space-y-3.5">
              <AnimatePresence initial={false}>
                {tab === "signup" && (
                  <motion.div
                    initial={{ opacity: 0, height: 0 }}
                    animate={{ opacity: 1, height: "auto" }}
                    exit={{ opacity: 0, height: 0 }}
                    transition={{ duration: 0.18 }}
                    className="overflow-hidden"
                  >
                    <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider" style={{ color: "#52525b" }}>
                      Ad soyad
                    </label>
                    <div className="relative">
                      <User className="absolute left-3.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2" style={{ color: "#3f3f46" }} />
                      <input
                        type="text"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        placeholder="Adinizi yazin"
                        className="w-full rounded-xl py-2.5 pl-10 pr-4 text-[13px] outline-none transition"
                        style={{
                          background: "rgba(255,255,255,0.04)",
                          border: "1px solid rgba(255,255,255,0.08)",
                          color: "#fafafa",
                        }}
                        onFocus={(e) => (e.currentTarget.style.borderColor = "rgba(110,215,206,0.28)")}
                        onBlur={(e) => (e.currentTarget.style.borderColor = "rgba(255,255,255,0.08)")}
                      />
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>

              {/* Email */}
              <div>
                <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider" style={{ color: "#52525b" }}>
                  E-posta
                </label>
                <div className="relative">
                  <Mail className="absolute left-3.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2" style={{ color: "#3f3f46" }} />
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="ornek@email.com"
                    className="w-full rounded-xl py-2.5 pl-10 pr-4 text-[13px] outline-none transition"
                    style={{
                      background: "rgba(255,255,255,0.04)",
                      border: "1px solid rgba(255,255,255,0.08)",
                      color: "#fafafa",
                    }}
                    onFocus={(e) => (e.currentTarget.style.borderColor = "rgba(110,215,206,0.28)")}
                    onBlur={(e) => (e.currentTarget.style.borderColor = "rgba(255,255,255,0.08)")}
                  />
                </div>
              </div>

              {/* Password */}
              <div>
                <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider" style={{ color: "#52525b" }}>
                  Sifre
                </label>
                <div className="relative">
                  <Lock className="absolute left-3.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2" style={{ color: "#3f3f46" }} />
                  <input
                    type={showPassword ? "text" : "password"}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="••••••••"
                    className="w-full rounded-xl py-2.5 pl-10 pr-11 text-[13px] outline-none transition"
                    style={{
                      background: "rgba(255,255,255,0.04)",
                      border: "1px solid rgba(255,255,255,0.08)",
                      color: "#fafafa",
                    }}
                    onFocus={(e) => (e.currentTarget.style.borderColor = "rgba(110,215,206,0.28)")}
                    onBlur={(e) => (e.currentTarget.style.borderColor = "rgba(255,255,255,0.08)")}
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    className="absolute right-3.5 top-1/2 -translate-y-1/2 transition-colors"
                    style={{ color: "#3f3f46" }}
                    aria-label={showPassword ? "Sifreyi gizle" : "Sifreyi goster"}
                    onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.color = "#a1a1aa")}
                    onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.color = "#3f3f46")}
                  >
                    {showPassword ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                  </button>
                </div>
              </div>

              {/* Submit */}
              <button
                type="submit"
                disabled={isLoading || !email || !password || (tab === "signup" && !name.trim())}
                className="mt-1 flex w-full items-center justify-center gap-2 rounded-xl py-3 text-[13.5px] font-semibold transition-opacity disabled:opacity-40 disabled:cursor-not-allowed"
                style={{ background: "#6ed7ce", color: "#041210" }}
                onMouseEnter={(e) => { if (!isLoading) (e.currentTarget as HTMLElement).style.opacity = "0.88"; }}
                onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.opacity = "1"; }}
              >
                {isLoading ? (
                  <span className="h-4 w-4 animate-spin rounded-full border-2 border-current/30 border-t-current" />
                ) : (
                  <>
                    {tab === "signin" ? "Giris yap" : "Hesap olustur"}
                    <ArrowRight className="h-4 w-4" />
                  </>
                )}
              </button>
            </form>

            <p className="mt-5 flex items-start justify-center gap-1.5 text-center text-[11px] leading-5" style={{ color: "#3f3f46" }}>
              <ShieldCheck className="mt-0.5 h-3.5 w-3.5 flex-none" style={{ color: "#52525b" }} />
              <span>Devam ederek kullanim ve gizlilik kosullarini kabul etmis olursun.</span>
            </p>
          </div>
        </motion.div>
      </div>
    </div>
  );
}
