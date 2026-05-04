import { useCallback, useEffect, useState } from "react";
import Editor from "@monaco-editor/react";
import {
  AlertCircle,
  ArrowLeft,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Code2,
  Loader2,
  Play,
  RotateCcw,
  Send,
  Terminal,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "framer-motion";
import { CodeAPI, LearningAPI } from "@/services/api";

interface InteractiveIDEProps {
  /** Kodu çalıştırma sonucunu AI chat'e göndermek için callback. */
  onSendToChat?: (message: string) => void;
  /** Aktif konu adı editör başlığında gösterilir. */
  topicTitle?: string;
  /** Aktif topic/session bilgisi IDE sonucunu öğrenme hafızasına bağlar. */
  topicId?: string;
  sessionId?: string;
  /** Algoritmik quiz sorusu görev kartı olarak gösterilir. */
  quizQuestion?: string;
  /** IDE kapanma callback'i chat'e dönüş için çağrılır. */
  onClose?: () => void;
}

const DEFAULT_CODE = `using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Merhaba, Orka!");
    }
}`;

const MONACO_LANG: Record<string, string> = {
  csharp: "csharp",
  bash: "shell",
  r: "r",
};

const LANGUAGE_OPTIONS = [
  { value: "csharp", label: "C#" },
  { value: "python", label: "Python" },
  { value: "javascript", label: "JavaScript" },
  { value: "typescript", label: "TypeScript" },
  { value: "java", label: "Java" },
  { value: "rust", label: "Rust" },
  { value: "go", label: "Go" },
  { value: "cpp", label: "C++" },
  { value: "c", label: "C" },
  { value: "kotlin", label: "Kotlin" },
  { value: "swift", label: "Swift" },
  { value: "php", label: "PHP" },
  { value: "ruby", label: "Ruby" },
  { value: "scala", label: "Scala" },
  { value: "r", label: "R" },
  { value: "bash", label: "Bash" },
];

const STARTER_CODE: Record<string, string> = {
  csharp: DEFAULT_CODE,
  python: `print("Merhaba, Orka!")`,
  javascript: `console.log("Merhaba, Orka!");`,
  typescript: `const msg: string = "Merhaba, Orka!";\nconsole.log(msg);`,
  java: `public class Main {\n    public static void main(String[] args) {\n        System.out.println("Merhaba, Orka!");\n    }\n}`,
  rust: `fn main() {\n    println!("Merhaba, Orka!");\n}`,
  go: `package main\n\nimport "fmt"\n\nfunc main() {\n    fmt.Println("Merhaba, Orka!")\n}`,
  cpp: `#include <iostream>\nusing namespace std;\n\nint main() {\n    cout << "Merhaba, Orka!" << endl;\n    return 0;\n}`,
  c: `#include <stdio.h>\n\nint main() {\n    printf("Merhaba, Orka!\\n");\n    return 0;\n}`,
  kotlin: `fun main() {\n    println("Merhaba, Orka!")\n}`,
  swift: `print("Merhaba, Orka!")`,
  php: `<?php\necho "Merhaba, Orka!\\n";\n?>`,
  ruby: `puts "Merhaba, Orka!"`,
  scala: `object Main extends App {\n    println("Merhaba, Orka!")\n}`,
  r: `cat("Merhaba, Orka!\\n")`,
  bash: `echo "Merhaba, Orka!"`,
};

const MAX_TUTOR_OUTPUT_CHARS = 4000;

const formatRunOutput = (stdout: string | null, stderr: string | null): string => {
  const parts: string[] = [];
  if (stdout?.trim()) parts.push(`Çıktı:\n${stdout.trim()}`);
  if (stderr?.trim()) parts.push(`Hata:\n${stderr.trim()}`);
  return parts.join("\n\n") || "(boş çıktı)";
};

const clipForTutor = (value: string): string => {
  if (value.length <= MAX_TUTOR_OUTPUT_CHARS) return value;
  return `${value.slice(0, MAX_TUTOR_OUTPUT_CHARS)}\n\n[Not: Çıktı çok uzun olduğu için Orka burada kırptı.]`;
};

export default function InteractiveIDE({ onSendToChat, topicTitle, topicId, sessionId, quizQuestion, onClose }: InteractiveIDEProps) {
  const [code, setCode] = useState(DEFAULT_CODE);
  const [language, setLanguage] = useState("csharp");
  const [running, setRunning] = useState(false);
  const [stdout, setStdout] = useState<string | null>(null);
  const [stderr, setStderr] = useState<string | null>(null);
  const [success, setSuccess] = useState<boolean | null>(null);
  const [outputOpen, setOutputOpen] = useState(true);

  const langLabel = LANGUAGE_OPTIONS.find((item) => item.value === language)?.label ?? language;
  const hasOutput = stdout !== null || stderr !== null || success !== null;
  const runSucceeded = success === true;
  const outputText = formatRunOutput(stdout, stderr);

  useEffect(() => {
    if (!onClose) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const resetOutput = useCallback(() => {
    setStdout(null);
    setStderr(null);
    setSuccess(null);
  }, []);

  const handleLanguageChange = useCallback((lang: string) => {
    setLanguage(lang);
    setCode(STARTER_CODE[lang] ?? "");
    resetOutput();
  }, [resetOutput]);

  const handleRun = useCallback(async () => {
    if (running || !code.trim()) return;
    setRunning(true);
    resetOutput();

    try {
      const result = await CodeAPI.run({ code, language, topicId, sessionId });
      setStdout(result.stdout || null);
      setStderr(result.stderr || null);
      setSuccess(Boolean(result.success));
      setOutputOpen(true);
    } catch (err: any) {
      const apiMessage = err?.response?.data?.stderr || err?.response?.data?.error;
      const fallbackMessage = apiMessage || "Kod çalıştırma servisine bağlanılamadı. Lütfen birkaç saniye sonra tekrar deneyin.";
      setStderr(fallbackMessage);
      setSuccess(false);
      setOutputOpen(true);
      void LearningAPI.recordSignal({
        topicId,
        sessionId,
        signalType: "IdeRunCompleted",
        skillTag: language,
        topicPath: quizQuestion ? "IDE > Quiz kod cevabı" : "IDE > Kod çalıştırma",
        score: 0,
        isPositive: false,
        payloadJson: JSON.stringify({
          language,
          success: false,
          errorKind: "frontend-api-failure",
          message: fallbackMessage,
          codeLength: code.length,
          quizQuestion: Boolean(quizQuestion),
        }),
      }).catch((err: unknown) => {
        console.error("[InteractiveIDE] recordSignal IdeRunCompleted failed:", err);
      });
    } finally {
      setRunning(false);
    }
  }, [code, language, quizQuestion, resetOutput, running, sessionId, topicId]);

  const handleResetCode = useCallback(() => {
    setCode(STARTER_CODE[language] ?? "");
    resetOutput();
  }, [language, resetOutput]);

  const handleSendToChat = useCallback(() => {
    if (!onSendToChat) return;

    const questionContext = quizQuestion ? `**Quiz Sorusu:** ${quizQuestion}\n\n` : "";
    const recordTutorSignal = () => {
      void LearningAPI.recordSignal({
        topicId,
        sessionId,
        signalType: "IdeSentToTutor",
        skillTag: language,
        topicPath: quizQuestion ? "IDE > Quiz kod cevabı" : "IDE > Kod inceleme",
        score: success === null ? undefined : success ? 100 : 0,
        isPositive: success === null ? undefined : success,
        payloadJson: JSON.stringify({
          language,
          hasOutput,
          success,
          quizQuestion: Boolean(quizQuestion),
          stdoutLength: stdout?.length ?? 0,
          stderrLength: stderr?.length ?? 0,
        }),
      }).catch((err: unknown) => {
        console.error("[InteractiveIDE] recordSignal IdeSentToTutor failed:", err);
      });
    };

    if (!hasOutput) {
      const message = [
        `${questionContext}İşte yazdığım ${langLabel} kodu:`,
        "```" + language,
        code,
        "```",
        "",
        "Kodu incele, çözüm mantığımı değerlendir ve eksik gördüğün yeri adım adım anlat.",
      ].join("\n");
      recordTutorSignal();
      onSendToChat(message);
      onClose?.();
      return;
    }

    const status = runSucceeded ? "başarıyla çalıştı" : "hata verdi";
    const message = [
      `${questionContext}Aşağıdaki ${langLabel} kodu ${status}:`,
      "```" + language,
      code,
      "```",
      "",
      runSucceeded ? "**Çıktı:**" : "**Hata Çıktısı:**",
      "```",
      clipForTutor(outputText),
      "```",
      "",
      runSucceeded
        ? "Bu kodu incele ve soruya verdiğim cevabın doğru olup olmadığını değerlendir. Daha iyi bir çözüm varsa göster."
        : "Bu hatayı açıkla, neden olduğunu söyle ve nasıl düzelteceğimi adım adım göster.",
    ].join("\n");

    recordTutorSignal();
    onSendToChat(message);
    onClose?.();
  }, [code, hasOutput, langLabel, language, onClose, onSendToChat, outputText, quizQuestion, runSucceeded, sessionId, stderr, stdout, success, topicId]);

  return (
    <div className="flex h-full flex-1 flex-col overflow-hidden bg-transparent text-[#172033]">
      <div className="flex-shrink-0 border-b border-[#526d82]/12 bg-[#f4f7f7]/82 px-4 py-3 backdrop-blur-xl md:px-5">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div className="flex min-w-0 items-center gap-3">
            <span className="grid h-10 w-10 place-items-center rounded-2xl bg-[#172033] text-white shadow-sm shadow-slate-900/10">
              <Terminal className="h-4 w-4" />
            </span>
            <div className="min-w-0">
              <p className="text-sm font-black tracking-tight text-[#172033]">İnteraktif Kod Editörü</p>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-[11px] font-bold text-[#667085]">
                <span>{topicTitle || "Serbest çalışma"}</span>
                <span className="h-1 w-1 rounded-full bg-[#b8c4cc]" />
                <span>{langLabel}</span>
                <span className="hidden rounded-full bg-[#dcecf3]/72 px-2 py-0.5 text-[#2d5870] sm:inline-flex">Escape ile kapanır</span>
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {onClose && (
              <button
                onClick={onClose}
                className="inline-flex items-center gap-2 rounded-xl border border-[#526d82]/14 bg-[#eef1f3]/76 px-3 py-2 text-xs font-extrabold text-[#344054] transition hover:-translate-y-0.5 hover:bg-[#e4eaec]"
                title="Sohbete dön"
              >
                <ArrowLeft className="h-3.5 w-3.5" />
                <span className="hidden sm:inline">Sohbete Dön</span>
              </button>
            )}

            <select
              value={language}
              onChange={(event) => handleLanguageChange(event.target.value)}
              className="rounded-xl border border-[#526d82]/16 bg-[#f7f4ec]/82 px-3 py-2 text-xs font-bold text-[#344054] outline-none transition focus:border-[#52768a]"
            >
              {LANGUAGE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </select>

            <button
              onClick={handleResetCode}
              className="inline-flex items-center gap-1.5 rounded-xl border border-[#526d82]/14 bg-[#eef1f3]/70 px-3 py-2 text-xs font-bold text-[#667085] transition hover:bg-[#e4eaec] hover:text-[#172033]"
              title="Editörü sıfırla"
            >
              <RotateCcw className="h-3.5 w-3.5" />
              <span className="hidden sm:inline">Sıfırla</span>
            </button>

            <button
              onClick={handleRun}
              disabled={running || !code.trim()}
              className={`inline-flex items-center gap-2 rounded-xl border px-4 py-2 text-xs font-extrabold transition-all duration-200 ${
                running || !code.trim()
                  ? "cursor-not-allowed border-[#526d82]/12 bg-[#e4eaec]/70 text-[#8a97a0]"
                  : "border-[#172033]/10 bg-[#172033] text-white shadow-sm shadow-slate-900/12 hover:-translate-y-0.5 hover:bg-[#24314b]"
              }`}
            >
              {running ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Play className="h-3.5 w-3.5" />}
              <span>{running ? "Çalışıyor..." : "Kodu Çalıştır"}</span>
            </button>

            {onSendToChat && (
              <button
                onClick={handleSendToChat}
                disabled={running || !code.trim()}
                className="inline-flex items-center gap-2 rounded-xl border border-[#8a6a33]/16 bg-[#fff8ee]/86 px-4 py-2 text-xs font-extrabold text-[#6d4f20] shadow-sm transition hover:-translate-y-0.5 hover:bg-[#efe1c9] disabled:cursor-not-allowed disabled:opacity-45"
                title="Kodu değerlendirmesi için hocaya gönder"
              >
                <Send className="h-3.5 w-3.5" />
                <span>Hocaya Gönder</span>
              </button>
            )}

            {onClose && (
              <button
                onClick={onClose}
                className="grid h-9 w-9 place-items-center rounded-xl border border-[#526d82]/12 bg-[#f7f4ec]/74 text-[#667085] transition hover:bg-[#f4e1dc] hover:text-[#9a4e3e]"
                aria-label="Kapat"
                title="Kapat"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>
        </div>
      </div>

      {quizQuestion && (
        <div className="flex-shrink-0 border-b border-[#526d82]/12 bg-[#fff8ee]/74 px-4 py-3 md:px-5">
          <div className="rounded-2xl border border-[#e8c46f]/24 bg-[#f7f4ec]/72 p-4">
            <div className="mb-2 flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#8a641f]">
              <Code2 className="h-3.5 w-3.5" />
              Görev Kartı
            </div>
            <p className="text-sm leading-relaxed text-[#344054]">{quizQuestion}</p>
          </div>
        </div>
      )}

      <div className="min-h-0 flex-1 bg-[#111827] p-2 md:p-3">
        <div className="h-full overflow-hidden rounded-2xl border border-white/10 bg-[#0f172a] shadow-inner shadow-black/30">
          <Editor
            height="100%"
            defaultLanguage="csharp"
            language={MONACO_LANG[language] ?? language}
            value={code}
            onChange={(value) => setCode(value ?? "")}
            theme="vs-dark"
            options={{
              fontSize: 14,
              lineHeight: 22,
              fontFamily: "'JetBrains Mono', 'Fira Code', Consolas, monospace",
              fontLigatures: true,
              minimap: { enabled: false },
              scrollBeyondLastLine: false,
              wordWrap: "on",
              padding: { top: 16, bottom: 16 },
              renderLineHighlight: "line",
              cursorBlinking: "smooth",
              smoothScrolling: true,
              automaticLayout: true,
              tabSize: 4,
              formatOnPaste: true,
            }}
          />
        </div>
      </div>

      <AnimatePresence initial={false}>
        {hasOutput && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.25, ease: "easeInOut" }}
            className="flex-shrink-0 overflow-hidden border-t border-[#526d82]/15 bg-[#eef1f3]/86"
          >
            <div className="flex items-center justify-between border-b border-[#526d82]/12 px-4 py-2">
              <div className="flex items-center gap-2">
                <span className={`grid h-7 w-7 place-items-center rounded-full ${runSucceeded ? "bg-[#d9e7de] text-[#547c61]" : "bg-[#f4e1dc] text-[#9a4e3e]"}`}>
                  {runSucceeded ? <CheckCircle2 className="h-4 w-4" /> : <AlertCircle className="h-4 w-4" />}
                </span>
                <span className="text-[11px] font-black uppercase tracking-[0.16em] text-[#667085]">
                  {runSucceeded ? "Çıktı" : "Hata Çıktısı"} · {langLabel}
                </span>
              </div>
              <div className="flex items-center gap-2">
                {onSendToChat && (
                  <button
                    onClick={handleSendToChat}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-[#526d82]/14 bg-[#f7f4ec]/74 px-3 py-1 text-[11px] font-bold text-[#667085] transition hover:bg-[#efe7d6] hover:text-[#172033]"
                  >
                    <Send className="h-3 w-3" />
                    Hocaya Sor
                  </button>
                )}
                <button
                  onClick={() => setOutputOpen((value) => !value)}
                  className="rounded-lg p-1 text-[#98a2b3] transition hover:bg-[#d7e6ec]/60 hover:text-[#667085]"
                  aria-label={outputOpen ? "Çıktıyı gizle" : "Çıktıyı göster"}
                >
                  {outputOpen ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronUp className="h-3.5 w-3.5" />}
                </button>
              </div>
            </div>

            <AnimatePresence initial={false}>
              {outputOpen && (
                <motion.div
                  initial={{ height: 0 }}
                  animate={{ height: "auto" }}
                  exit={{ height: 0 }}
                  transition={{ duration: 0.2 }}
                  className="overflow-hidden"
                >
                  <pre className={`max-h-48 overflow-y-auto overflow-x-auto whitespace-pre-wrap px-4 py-3 text-[13px] font-mono leading-relaxed ${runSucceeded ? "text-[#456f55]" : "text-[#9a4e3e]"}`}>
                    {outputText}
                  </pre>
                </motion.div>
              )}
            </AnimatePresence>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
