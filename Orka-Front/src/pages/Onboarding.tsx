import React, { useState, useEffect, useRef } from "react";
import { useLocation } from "wouter";
import toast from "react-hot-toast";
import { UserAPI } from "../services/api";
import { useAuth } from "../contexts/AuthContext";
import { useTheme } from "../contexts/ThemeContext";
import OrcaLogo from "../components/OrcaLogo";
import "./Onboarding.css";

type PathPref = "standard" | "accelerated";
type LearnStyle = "theoretical" | "practical";
type QuizOption = "A" | "B" | "C";
type DailyCommit = "15m" | "30m" | "45m" | "60m";

export default function Onboarding() {
  const [, setLocation] = useLocation();
  const { syncOnboardingCompleted } = useAuth();
  const { setTheme, resolvedTheme } = useTheme();

  // Onboarding state
  const [currentStep, setCurrentStep] = useState<number>(1);
  const [pathPref, setPathPref] = useState<PathPref | null>(null);
  const [learningStyle, setLearningStyle] = useState<LearnStyle | null>(null);
  const [quizOption, setQuizOption] = useState<QuizOption | null>(null);
  const [dailyCommit, setDailyCommit] = useState<DailyCommit>("30m");

  // Theme Wipe animation states
  const [wipeActive, setWipeActive] = useState(false);
  const [wipeCoords, setWipeCoords] = useState({ x: 0, y: 0, bg: "#070809" });

  // Syncing logs states
  const [syncLogs, setSyncLogs] = useState<string[]>([]);
  const [syncIndex, setSyncLogIndex] = useState(0);

  // References for coordinate tracking
  const containerRef = useRef<HTMLDivElement>(null);

  // Milestone mapping based on daily commitment
  const milestones: Record<DailyCommit, string> = {
    "15m": "Steady sprint: Acquire core foundational blocks in 30 days.",
    "30m": "Balanced climb: Achieve deep mastery within 15 days.",
    "45m": "Accelerated ascent: Master advanced reactive paradigms in 10 days.",
    "60m": "Hyper-speed ascension: Unlocked elite engineer status in 7 days.",
  };

  // Syncing HUD status logs
  const statusLogs = [
    "Analyzing your diagnostic responses...",
    "Synthesizing learning path preferences...",
    "Calibrating adaptive study engine...",
    "Generating customized curriculum...",
    "Saving profile to Orka cloud core...",
    "Syncing onboarding status...",
  ];

  // Progressive logger in Step 5
  useEffect(() => {
    if (currentStep !== 5) return;

    if (syncIndex < statusLogs.length) {
      const timer = setTimeout(() => {
        setSyncLogs((prev) => [...prev, statusLogs[syncIndex]]);
        setSyncLogIndex((prev) => prev + 1);
      }, 700);
      return () => clearTimeout(timer);
    } else {
      // Trigger actual payload submission once logs finish
      const submitOnboardingData = async () => {
        try {
          let measuredLevel = "Novice";
          let correctCount = 0;
          if (quizOption === "A") {
            measuredLevel = "Advanced";
            correctCount = 1;
          } else if (quizOption === "B") {
            measuredLevel = "Intermediate";
          }

          const payload = {
            answeredCount: 1,
            correctCount,
            measuredLevel,
            learningStyle: learningStyle || "theoretical",
            pathPreference: pathPref || "standard",
            theme: learningStyle === "practical" ? "Light" as const : "Dark" as const,
          };

          const { data: updatedUser } = await UserAPI.saveOnboarding(payload);
          toast.success("Profiliniz başarıyla oluşturuldu!");
          syncOnboardingCompleted(updatedUser);
          setLocation("/app");
        } catch (err) {
          console.error(err);
          toast.error("Profil senkronizasyonu başarısız oldu. Lütfen tekrar deneyin.");
          // Go back to commitment step to allow retry
          setCurrentStep(4);
          setSyncLogs([]);
          setSyncLogIndex(0);
        }
      };
      submitOnboardingData();
    }
  }, [currentStep, syncIndex]);

  // Handle Step 2 Theme-Wipe transition
  const handleLearningStyleSelect = (e: React.MouseEvent, style: LearnStyle) => {
    setLearningStyle(style);

    const x = e.clientX || window.innerWidth / 2;
    const y = e.clientY || window.innerHeight / 2;
    const targetTheme = style === "theoretical" ? "Dark" : "Light";
    const targetBg = targetTheme === "Dark" ? "#070809" : "#ffffff";

    setWipeCoords({ x, y, bg: targetBg });
    setWipeActive(true);

    // Call setTheme to toggle DOM theme immediately
    setTheme(targetTheme);

    // Allow radial wipe transition to visually finish before pushing step
    setTimeout(() => {
      setWipeActive(false);
      setCurrentStep(3);
    }, 850);
  };

  // Step render functions
  const renderStepIndicator = () => {
    const totalSteps = 5;
    return (
      <div className="w-full max-w-xl mx-auto mb-8">
        <div className="flex justify-between items-center text-xs font-semibold mb-2 tracking-widest text-emerald-400 uppercase">
          <span>Aşama {currentStep} / {totalSteps}</span>
          <span>{Math.round(((currentStep - 1) / (totalSteps - 1)) * 100)}% Tamamlandı</span>
        </div>
        <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden relative">
          <div
            className="h-full bg-gradient-to-r from-teal-400 to-emerald-500 rounded-full transition-all duration-500 ease-out shadow-[0_0_10px_rgba(110,215,206,0.5)]"
            style={{ width: `${((currentStep - 1) / (totalSteps - 1)) * 100}%` }}
          />
        </div>
      </div>
    );
  };

  return (
    <div
      ref={containerRef}
      className={`min-h-screen w-full flex flex-col items-center justify-between p-6 transition-colors duration-500 ${
        resolvedTheme === "dark" ? "bg-[#070809] text-white" : "bg-slate-50 text-slate-900"
      }`}
    >
      {/* Theme Wipe Clip Transition Element */}
      <div
        className={`theme-wipe-overlay ${wipeActive ? "theme-wipe-active" : ""}`}
        style={{
          // @ts-ignore
          "--wipe-x": `${wipeCoords.x}px`,
          "--wipe-y": `${wipeCoords.y}px`,
          "--wipe-bg": wipeCoords.bg,
        }}
      />

      {/* Header */}
      <header className="w-full flex items-center justify-between max-w-6xl mx-auto py-2">
        <div className="flex items-center gap-3">
          <div className="h-9 w-9 bg-emerald-400 text-[#041210] flex items-center justify-center rounded-xl shadow-lg shadow-emerald-400/20">
            <OrcaLogo className="h-5 w-5" />
          </div>
          <span className="font-extrabold text-lg tracking-wider bg-gradient-to-r from-emerald-400 to-teal-300 bg-clip-text text-transparent">
            ORKA
          </span>
        </div>
        {currentStep > 1 && currentStep < 5 && (
          <button
            onClick={() => setCurrentStep((prev) => prev - 1)}
            className={`text-sm font-medium transition-colors hover:text-emerald-400 flex items-center gap-1 ${
              resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-600"
            }`}
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 19l-7-7 7-7" />
            </svg>
            Geri
          </button>
        )}
      </header>

      {/* Main Flow Area */}
      <main className="flex-1 w-full max-w-4xl flex flex-col justify-center py-10 z-10">
        {currentStep < 5 && renderStepIndicator()}

        <div className="step-enter step-enter-active">
          {/* STEP 1: PATH PREFERENCE */}
          {currentStep === 1 && (
            <div className="text-center">
              <h2 className="text-3xl md:text-4xl font-extrabold tracking-tight mb-3">
                Nasıl bir öğrenim patikası tercih edersiniz?
              </h2>
              <p className={`text-sm md:text-base max-w-2xl mx-auto mb-10 ${
                resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-500"
              }`}>
                Öğrenme ritminizi ve hedeflerinizi belirleyin. Sizin için optimize edilmiş bir program hazırlayalım.
              </p>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-6 max-w-3xl mx-auto">
                {/* Standard Card */}
                <div
                  onClick={() => {
                    setPathPref("standard");
                    setCurrentStep(2);
                  }}
                  className={`onboarding-glass cursor-pointer rounded-2xl p-8 flex flex-col items-center text-center ${
                    pathPref === "standard" ? "onboarding-glass-selected" : ""
                  }`}
                >
                  <div className="h-16 w-16 bg-emerald-400/10 text-emerald-400 rounded-full flex items-center justify-center mb-6">
                    <svg className="h-8 w-8" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4" />
                    </svg>
                  </div>
                  <h3 className="text-xl font-bold mb-3">Yapılandırılmış Standart Büyüme</h3>
                  <p className={`text-sm leading-relaxed ${
                    resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-500"
                  }`}>
                    Aralıklı tekrarlar, zengin alıştırma setleri ve sistematik kazanımlarla her konuyu sindirerek derinlemesine öğrenin.
                  </p>
                </div>

                {/* Accelerated Card */}
                <div
                  onClick={() => {
                    setPathPref("accelerated");
                    setCurrentStep(2);
                  }}
                  className={`onboarding-glass cursor-pointer rounded-2xl p-8 flex flex-col items-center text-center ${
                    pathPref === "accelerated" ? "onboarding-glass-selected" : ""
                  }`}
                >
                  <div className="h-16 w-16 bg-teal-400/10 text-teal-400 rounded-full flex items-center justify-center mb-6">
                    <svg className="h-8 w-8" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M13 10V3L4 14h7v7l9-11h-7z" />
                    </svg>
                  </div>
                  <h3 className="text-xl font-bold mb-3">Hızlandırılmış Hyper-Sprint</h3>
                  <p className={`text-sm leading-relaxed ${
                    resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-500"
                  }`}>
                    İleri düzey reaktif şablonlara, mimari yapılara ve pratik kodlama stratejilerine odaklanan yüksek tempolu süreç.
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* STEP 2: LEARNING STYLE */}
          {currentStep === 2 && (
            <div className="text-center">
              <h2 className="text-3xl md:text-4xl font-extrabold tracking-tight mb-3">
                Hangi öğrenme modeli sizi daha iyi tanımlıyor?
              </h2>
              <p className={`text-sm md:text-base max-w-2xl mx-auto mb-10 ${
                resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-500"
              }`}>
                Teorik derinlik mi yoksa pratik uygulama odaklılık mı? Seçiminiz arayüz modunu da dinamik olarak uyarlayacaktır.
              </p>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-6 max-w-3xl mx-auto">
                {/* Theoretical Card */}
                <div
                  onClick={(e) => handleLearningStyleSelect(e, "theoretical")}
                  className={`onboarding-glass cursor-pointer rounded-2xl p-8 flex flex-col items-center text-center ${
                    learningStyle === "theoretical" ? "onboarding-glass-selected" : ""
                  }`}
                >
                  <div className="h-16 w-16 bg-blue-400/10 text-blue-400 rounded-full flex items-center justify-center mb-6">
                    <svg className="h-8 w-8" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
                    </svg>
                  </div>
                  <h3 className="text-xl font-bold mb-3">Teorik ve Kavramsal (Koyu Tema)</h3>
                  <p className={`text-sm leading-relaxed ${
                    resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-500"
                  }`}>
                    Mekanizmaların arkasındaki matematiksel temelleri, durum makinelerini ve soyut akış modellerini inceleyin.
                  </p>
                </div>

                {/* Practical Card */}
                <div
                  onClick={(e) => handleLearningStyleSelect(e, "practical")}
                  className={`onboarding-glass cursor-pointer rounded-2xl p-8 flex flex-col items-center text-center ${
                    learningStyle === "practical" ? "onboarding-glass-selected" : ""
                  }`}
                >
                  <div className="h-16 w-16 bg-amber-400/10 text-amber-400 rounded-full flex items-center justify-center mb-6">
                    <svg className="h-8 w-8" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
                    </svg>
                  </div>
                  <h3 className="text-xl font-bold mb-3">Pratik ve Uygulamalı (Açık Tema)</h3>
                  <p className={`text-sm leading-relaxed ${
                    resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-500"
                  }`}>
                    Doğrudan kod yazarak, sandbox ortamlarında test ederek ve dinamik hata ayıklama senaryolarıyla öğrenin.
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* STEP 3: DIAGNOSTIC QUIZ */}
          {currentStep === 3 && (
            <div className="max-w-2xl mx-auto">
              <div className="text-center mb-10">
                <span className="text-xs font-bold tracking-widest text-emerald-400 uppercase bg-emerald-400/10 px-3 py-1.5 rounded-full mb-3 inline-block">
                  Hızlı Değerlendirme
                </span>
                <h2 className="text-2xl md:text-3xl font-extrabold tracking-tight">
                  Seviyenizi belirlemek için bir sorumuz var:
                </h2>
              </div>

              {/* Quiz HUD */}
              <div className="onboarding-glass rounded-3xl p-6 md:p-8 shadow-xl relative overflow-hidden">
                <p className="text-lg font-bold mb-8 text-center border-b border-zinc-800 pb-5">
                  How do you preferred handling complex reactive asynchronous streams?
                </p>

                <div className="flex flex-col gap-4">
                  {/* Option A */}
                  <div
                    onClick={() => setQuizOption("A")}
                    className={`p-5 rounded-2xl border transition-all duration-300 cursor-pointer flex items-center justify-between ${
                      quizOption === "A"
                        ? "border-emerald-400 bg-emerald-500/10 translate-x-2 shadow-[0_0_15px_rgba(110,215,206,0.15)]"
                        : quizOption !== null
                        ? "border-zinc-800/40 opacity-40 blur-[0.8px]"
                        : "border-zinc-800 hover:border-zinc-700 bg-black/10"
                    }`}
                  >
                    <div className="flex items-start gap-4">
                      <span className={`h-7 w-7 rounded-lg flex items-center justify-center text-sm font-bold shrink-0 ${
                        quizOption === "A" ? "bg-emerald-400 text-[#041210]" : "bg-zinc-800 text-zinc-400"
                      }`}>
                        A
                      </span>
                      <p className="text-sm md:text-base font-medium">
                        Isolate them into atomic, deterministic state machine modules.
                      </p>
                    </div>
                    {quizOption === "A" && (
                      <div className="h-6 w-6 text-emerald-400 shrink-0">
                        <svg fill="none" stroke="currentColor" viewBox="0 0 24 24" className="w-6 h-6">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.5" d="M5 13l4 4L19 7" />
                        </svg>
                      </div>
                    )}
                  </div>

                  {/* Option B */}
                  <div
                    onClick={() => setQuizOption("B")}
                    className={`p-5 rounded-2xl border transition-all duration-300 cursor-pointer flex items-center justify-between ${
                      quizOption === "B"
                        ? "border-emerald-400 bg-emerald-500/10 translate-x-2 shadow-[0_0_15px_rgba(110,215,206,0.15)]"
                        : quizOption !== null
                        ? "border-zinc-800/40 opacity-40 blur-[0.8px]"
                        : "border-zinc-800 hover:border-zinc-700 bg-black/10"
                    }`}
                  >
                    <div className="flex items-start gap-4">
                      <span className={`h-7 w-7 rounded-lg flex items-center justify-center text-sm font-bold shrink-0 ${
                        quizOption === "B" ? "bg-emerald-400 text-[#041210]" : "bg-zinc-800 text-zinc-400"
                      }`}>
                        B
                      </span>
                      <p className="text-sm md:text-base font-medium">
                        Write inline responsive hooks for immediate interface side effects.
                      </p>
                    </div>
                    {quizOption === "B" && (
                      <div className="h-6 w-6 text-emerald-400 shrink-0">
                        <svg fill="none" stroke="currentColor" viewBox="0 0 24 24" className="w-6 h-6">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.5" d="M5 13l4 4L19 7" />
                        </svg>
                      </div>
                    )}
                  </div>

                  {/* Option C */}
                  <div
                    onClick={() => setQuizOption("C")}
                    className={`p-5 rounded-2xl border transition-all duration-300 cursor-pointer flex items-center justify-between ${
                      quizOption === "C"
                        ? "border-emerald-400 bg-emerald-500/10 translate-x-2 shadow-[0_0_15px_rgba(110,215,206,0.15)]"
                        : quizOption !== null
                        ? "border-zinc-800/40 opacity-40 blur-[0.8px]"
                        : "border-zinc-800 hover:border-zinc-700 bg-black/10"
                    }`}
                  >
                    <div className="flex items-start gap-4">
                      <span className={`h-7 w-7 rounded-lg flex items-center justify-center text-sm font-bold shrink-0 ${
                        quizOption === "C" ? "bg-emerald-400 text-[#041210]" : "bg-zinc-800 text-zinc-400"
                      }`}>
                        C
                      </span>
                      <p className="text-sm md:text-base font-medium">
                        Offload operations to multithreaded cloud worker architectures.
                      </p>
                    </div>
                    {quizOption === "C" && (
                      <div className="h-6 w-6 text-emerald-400 shrink-0">
                        <svg fill="none" stroke="currentColor" viewBox="0 0 24 24" className="w-6 h-6">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.5" d="M5 13l4 4L19 7" />
                        </svg>
                      </div>
                    )}
                  </div>
                </div>

                {quizOption !== null && (
                  <div className="mt-8 flex justify-center">
                    <button
                      onClick={() => setCurrentStep(4)}
                      className="btn-shimmer px-8 py-3 bg-emerald-400 text-[#041210] rounded-xl font-bold tracking-wider hover:bg-emerald-300 transition-colors shadow-lg shadow-emerald-400/20"
                    >
                      KAYDET VE DEVAM ET
                    </button>
                  </div>
                )}
              </div>
            </div>
          )}

          {/* STEP 4: DAILY COMMITMENT */}
          {currentStep === 4 && (
            <div className="text-center max-w-xl mx-auto">
              <h2 className="text-3xl font-extrabold tracking-tight mb-3">
                Günlük çalışma hedefiniz nedir?
              </h2>
              <p className={`text-sm mb-10 ${
                resolvedTheme === "dark" ? "text-zinc-400" : "text-slate-500"
              }`}>
                Size en uygun çalışma temposunu seçin. Süreye göre tahmini gelişim hızınızı aşağıda görebilirsiniz.
              </p>

              {/* Segmented Chip Buttons */}
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-10">
                {(["15m", "30m", "45m", "60m"] as DailyCommit[]).map((time) => (
                  <button
                    key={time}
                    onClick={() => setDailyCommit(time)}
                    className={`py-3.5 px-5 rounded-xl font-bold text-sm tracking-wide transition-all border ${
                      dailyCommit === time
                        ? "bg-emerald-400 border-emerald-400 text-[#041210] shadow-lg shadow-emerald-400/20"
                        : resolvedTheme === "dark"
                        ? "bg-zinc-900 border-zinc-800 text-zinc-300 hover:border-zinc-700"
                        : "bg-white border-slate-200 text-slate-700 hover:border-slate-300 hover:bg-slate-50"
                    }`}
                  >
                    {time === "60m" ? "60m+" : `${time.replace("m", "")} Dakika`}
                  </button>
                ))}
              </div>

              {/* Dynamic Estimated Milestones Area */}
              <div className="onboarding-glass rounded-2xl p-6 text-center mb-10 min-h-[100px] flex flex-col justify-center relative">
                <div className="absolute top-0 left-0 w-2 h-full bg-emerald-400 rounded-l-2xl" />
                <p className="text-xs tracking-widest text-emerald-400 font-bold uppercase mb-2">
                  Tahmini Hedef Süresi
                </p>
                <p className="text-base md:text-lg font-extrabold leading-relaxed px-2">
                  {milestones[dailyCommit]}
                </p>
              </div>

              {/* Animated progress bar representation based on selection */}
              <div className="mb-10">
                <div className="flex justify-between items-center text-xs text-zinc-500 font-semibold mb-2">
                  <span>Antrenman Yoğunluğu</span>
                  <span className="text-emerald-400">
                    {dailyCommit === "15m" && "Hafif Ritim"}
                    {dailyCommit === "30m" && "Dengeli Tempo"}
                    {dailyCommit === "45m" && "Hırslı Ritim"}
                    {dailyCommit === "60m" && "Maksimum Performans"}
                  </span>
                </div>
                <div className="h-3 w-full bg-zinc-800/40 rounded-full overflow-hidden border border-zinc-800 p-0.5">
                  <div
                    className="h-full bg-gradient-to-r from-teal-400 to-emerald-400 rounded-full transition-all duration-500 ease-out"
                    style={{
                      width:
                        dailyCommit === "15m"
                          ? "25%"
                          : dailyCommit === "30m"
                          ? "50%"
                          : dailyCommit === "45m"
                          ? "75%"
                          : "100%",
                    }}
                  />
                </div>
              </div>

              <button
                onClick={() => setCurrentStep(5)}
                className="btn-shimmer px-10 py-4 bg-emerald-400 text-[#041210] rounded-xl font-bold tracking-wider hover:bg-emerald-300 transition-colors shadow-lg shadow-emerald-400/20"
              >
                PATİKAYI BAŞLAT
              </button>
            </div>
          )}

          {/* STEP 5: SYNCING HUD */}
          {currentStep === 5 && (
            <div className="text-center max-w-lg mx-auto flex flex-col items-center">
              <h2 className="text-2xl md:text-3xl font-extrabold tracking-tight mb-10">
                Profiliniz Hazırlanıyor
              </h2>

              {/* Opposing rotating concentric rings & pulsing orb */}
              <div className="relative h-44 w-44 flex items-center justify-center mb-10">
                {/* Pulsing Gradient Orb */}
                <div className="absolute h-24 w-24 rounded-full bg-gradient-to-br from-emerald-400 to-teal-500 animate-pulse-glow" />

                {/* Outer Ring Rotating Clockwise */}
                <svg className="absolute inset-0 w-full h-full animate-spin" viewBox="0 0 100 100">
                  <circle
                    cx="50"
                    cy="50"
                    r="44"
                    fill="none"
                    stroke="url(#outerGrad)"
                    strokeWidth="3.5"
                    strokeDasharray="180 50"
                    strokeLinecap="round"
                  />
                  <defs>
                    <linearGradient id="outerGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                      <stop offset="0%" stopColor="#34d399" />
                      <stop offset="100%" stopColor="#14b8a6" />
                    </linearGradient>
                  </defs>
                </svg>

                {/* Inner Ring Rotating Counter-Clockwise */}
                <svg
                  className="absolute inset-2 w-[calc(100%-16px)] h-[calc(100%-16px)] animate-spin-reverse"
                  viewBox="0 0 100 100"
                >
                  <circle
                    cx="50"
                    cy="50"
                    r="40"
                    fill="none"
                    stroke="url(#innerGrad)"
                    strokeWidth="3"
                    strokeDasharray="100 40"
                    strokeLinecap="round"
                  />
                  <defs>
                    <linearGradient id="innerGrad" x1="100%" y1="100%" x2="0%" y2="0%">
                      <stop offset="0%" stopColor="#2dd4bf" />
                      <stop offset="100%" stopColor="#059669" />
                    </linearGradient>
                  </defs>
                </svg>

                {/* Centered Logo */}
                <div className="z-10 text-[#041210]">
                  <OrcaLogo className="h-10 w-10 text-[#041210]" />
                </div>
              </div>

              {/* Terminal Logs HUD */}
              <div className="w-full bg-[#0a0c10] border border-zinc-800/80 rounded-2xl p-5 text-left font-mono text-xs md:text-sm text-zinc-400 max-h-[180px] overflow-y-auto shadow-inner flex flex-col gap-2.5">
                {syncLogs.map((log, index) => (
                  <div key={index} className="flex items-center gap-2">
                    <span className="text-emerald-400 shrink-0">&gt;</span>
                    <span>{log}</span>
                    {index === syncLogs.length - 1 && syncIndex < statusLogs.length && (
                      <span className="h-1.5 w-1.5 bg-emerald-400 rounded-full animate-ping ml-1" />
                    )}
                  </div>
                ))}
                {syncIndex >= statusLogs.length && (
                  <div className="text-emerald-400 font-bold flex items-center gap-2 mt-2 border-t border-zinc-900 pt-2">
                    <span>[BAŞARILI]</span>
                    <span>Kurulum tamamlandı. Ana panele aktarılıyorsunuz...</span>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      </main>

      {/* Footer */}
      <footer className="w-full text-center py-4 text-xs text-zinc-500 font-medium max-w-6xl mx-auto border-t border-zinc-900/10 dark:border-zinc-800/20">
        © 2026 Orka AI. Tüm hakları saklıdır. Premium Adaptive Learning Workspace.
      </footer>
    </div>
  );
}
