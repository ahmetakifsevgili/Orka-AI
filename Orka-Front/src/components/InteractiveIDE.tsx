/*
 * InteractiveIDE — Monaco tabanlı kod editörü + Piston API çalıştırıcısı.
 * Öğrenci kodu yazar → "Kodu Çalıştır" → stdout/stderr terminal blokta görünür.
 * "AI'a Sor" butonu çıktıyı TutorAgent'a context olarak gönderir.
 */

import { useState, useCallback, useRef } from "react";
import Editor from "@monaco-editor/react";
import { Play, Loader2, Send, Terminal, ChevronDown, ChevronUp, RotateCcw, X } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import { CodeAPI } from "@/services/api";

interface InteractiveIDEProps {
  /** Kodu çalıştırma sonucunu AI chat'e göndermek için callback */
  onSendToChat?: (message: string) => void;
  /** Aktif konu adı — editör başlığında gösterilir */
  topicTitle?: string;
  /** Algoritmik quiz sorusu — editör başlığında görev olarak gösterilir */
  quizQuestion?: string;
  /** IDE kapanma callback'i — quiz gönderildikten sonra çağrılır */
  onClose?: () => void;
  /** Aktif session ID — Piston sonucu Redis'e yazılır, TutorAgent kod bağlamını okur */
  sessionId?: string;
}

const DEFAULT_CODE = `using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Merhaba, Orka!");
    }
}`;

/** Orka dil değeri → Monaco Editor dil ID eşlemesi (farklı olanlar) */
const MONACO_LANG: Record<string, string> = {
  csharp: "csharp",
  bash:   "shell",
  r:      "r",
};

const LANGUAGE_OPTIONS = [
  { value: "csharp",     label: "C#" },
  { value: "python",     label: "Python" },
  { value: "javascript", label: "JavaScript" },
  { value: "typescript", label: "TypeScript" },
  { value: "java",       label: "Java" },
  { value: "rust",       label: "Rust" },
  { value: "go",         label: "Go" },
  { value: "cpp",        label: "C++" },
  { value: "c",          label: "C" },
  { value: "kotlin",     label: "Kotlin" },
  { value: "swift",      label: "Swift" },
  { value: "php",        label: "PHP" },
  { value: "ruby",       label: "Ruby" },
  { value: "scala",      label: "Scala" },
  { value: "r",          label: "R" },
  { value: "bash",       label: "Bash" },
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

export default function InteractiveIDE({ onSendToChat, topicTitle, quizQuestion, onClose, sessionId }: InteractiveIDEProps) {
  const [code, setCode]         = useState(DEFAULT_CODE);
  const [language, setLanguage] = useState("csharp");
  const [running, setRunning]   = useState(false);
  const [stdout, setStdout]     = useState<string | null>(null);
  const [stderr, setStderr]     = useState<string | null>(null);
  const [success, setSuccess]   = useState<boolean | null>(null);
  const [outputOpen, setOutputOpen] = useState(true);
  const editorRef = useRef<unknown>(null);

  const handleLanguageChange = useCallback((lang: string) => {
    setLanguage(lang);
    setCode(STARTER_CODE[lang] ?? "");
    setStdout(null);
    setStderr(null);
    setSuccess(null);
  }, []);

  const handleRun = useCallback(async () => {
    if (running || !code.trim()) return;
    setRunning(true);
    setStdout(null);
    setStderr(null);
    setSuccess(null);

    try {
      const result = await CodeAPI.run({ code, language, sessionId });
      setStdout(result.stdout || null);
      setStderr(result.stderr || null);
      setSuccess(result.success);
      setOutputOpen(true);
    } catch (err: any) {
      // API'den gelen hata mesajını al (PistonService graceful response dönüyor)
      const apiMessage = err?.response?.data?.stderr || err?.response?.data?.error;
      setStderr(apiMessage || "Kod çalıştırma servisine bağlanılamadı. Lütfen tekrar deneyin.");
      setSuccess(false);
      setOutputOpen(true);
    } finally {
      setRunning(false);
    }
  }, [code, language, running]);

  const hasOutput = stdout !== null || stderr !== null;
  const langLabel = LANGUAGE_OPTIONS.find(l => l.value === language)?.label ?? language;

  const handleSendToChat = useCallback(() => {
    if (!onSendToChat) return;

    const questionContext = quizQuestion
      ? `**Quiz Sorusu:** ${quizQuestion}\n**Kullanılan Dil:** ${langLabel}\n\n`
      : "";

    if (!hasOutput) {
       const message = [
         `${questionContext}İşte yazdığım ${langLabel} kodu:`,
         "```" + language,
         code,
         "```",
         "",
         "Kodu incele ve yazdığım kadar bana geri bildirim ver veya soruya cevabımı değerlendir."
       ].join("\n");
       onSendToChat(message);
       onClose?.();
       return;
    }

    const output = stdout || stderr || "(çıktı yok)";
    const status = success ? "başarıyla çalıştı" : "hata verdi";
    const message = [
      `${questionContext}Aşağıdaki ${langLabel} kodu ${status}:`,
      "```" + language,
      code,
      "```",
      "",
      success ? "**Çıktı:**" : "**Hata Çıktısı:**",
      "```",
      output,
      "```",
      "",
      success
        ? "Bu kodu incele ve soruya verdiğim cevabı doğru olup olmadığına göre değerlendir."
        : "Bu hatayı açıkla ve nasıl düzelteceğimi göster."
    ].join("\n");

    onSendToChat(message);
    onClose?.();
  }, [code, stdout, stderr, success, language, onSendToChat, hasOutput, langLabel, quizQuestion, onClose]);

  return (
    <div className="flex-1 flex flex-col soft-page h-full overflow-hidden">
      {/* Header */}
      <div className="flex-shrink-0 flex items-center justify-between px-6 py-3 border-b soft-border soft-surface">
        <div className="flex items-center gap-3">
          <Terminal className="w-4 h-4 soft-text-muted" />
          <span className="text-sm font-medium text-foreground">
            İnteraktif Kod Editörü
          </span>
          {topicTitle && (
            <span className="text-xs soft-text-muted pl-2 border-l soft-border">
              {topicTitle}
            </span>
          )}
        </div>

        {/* Language selector */}
        <div className="flex items-center gap-3">
          <select
            value={language}
            onChange={(e) => handleLanguageChange(e.target.value)}
            className="text-xs soft-surface border rounded-lg px-3 py-1.5 focus:outline-none focus:border-emerald-500/50 transition-colors"
          >
            {LANGUAGE_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>

          <button
            onClick={() => { setCode(STARTER_CODE[language] ?? ""); setStdout(null); setStderr(null); setSuccess(null); }}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs soft-text-muted hover:text-foreground hover:bg-surface-muted border soft-border transition-colors"
            title="Editörü sıfırla"
          >
            <RotateCcw className="w-3.5 h-3.5" />
          </button>

          <button
            onClick={handleRun}
            disabled={running || !code.trim()}
            className={`
              flex items-center gap-2 px-4 py-1.5 rounded-lg text-xs font-semibold transition-all duration-200 border
              ${running || !code.trim()
                ? "soft-muted border-soft-border soft-text-muted cursor-not-allowed opacity-60"
                : "bg-emerald-500/10 border-emerald-500/25 text-emerald-700 dark:text-emerald-300 hover:bg-emerald-500/15"}
            `}
          >
            {running
              ? <><Loader2 className="w-3.5 h-3.5 animate-spin" /><span>Çalışıyor...</span></>
              : <><Play className="w-3.5 h-3.5" /><span>Çalıştır</span></>
            }
          </button>
          
          {onSendToChat && (
            <button
              onClick={handleSendToChat}
              disabled={running || !code.trim()}
              className="flex items-center gap-2 px-4 py-1.5 rounded-lg text-xs font-semibold bg-amber-500/10 border border-amber-500/25 text-amber-700 dark:text-amber-300 hover:bg-amber-500/15 transition-all duration-200 disabled:opacity-40 disabled:cursor-not-allowed"
              title="Kodu değerlendirmesi için eğitmene gönder"
            >
              <Send className="w-3.5 h-3.5" />
              <span>Hocaya Gönder</span>
            </button>
          )}

          {/* Kapat / Küçült butonu */}
          {onClose && (
            <button
              onClick={onClose}
              className="ml-2 flex items-center justify-center p-1.5 soft-text-muted hover:text-foreground hover:bg-surface-muted rounded-lg transition-colors"
              title="Editörü Kapat / Küçült"
            >
              <X className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>

      {/* Quiz Sorusu Banner'ı */}
      {quizQuestion && (
        <div className="flex-shrink-0 px-6 py-3 bg-amber-950/25 border-b border-amber-900/30">
          <p className="text-[11px] font-semibold text-amber-400 uppercase tracking-wider mb-1">Çözmen Gereken Soru</p>
          <p className="text-sm text-foreground leading-relaxed">{quizQuestion}</p>
        </div>
      )}

      {/* Editor area */}
      <div className="flex-1 min-h-0 overflow-hidden">
        <Editor
          height="100%"
          defaultLanguage="csharp"
          language={MONACO_LANG[language] ?? language}
          value={code}
          onChange={(val) => setCode(val ?? "")}
          onMount={(editor) => { editorRef.current = editor; }}
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

      {/* Output panel */}
      <AnimatePresence initial={false}>
        {hasOutput && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.25, ease: "easeInOut" }}
            className="flex-shrink-0 border-t soft-border soft-surface overflow-hidden"
          >
            {/* Output header */}
            <div className="flex items-center justify-between px-4 py-2 border-b soft-border">
              <div className="flex items-center gap-2">
                <div className={`w-2 h-2 rounded-full ${success ? "bg-emerald-500" : "bg-amber-500"}`} />
                <span className="text-[11px] font-mono font-medium soft-text-muted uppercase tracking-wider">
                  {success ? "Çıktı" : "Hata"} — {langLabel}
                </span>
              </div>
              <div className="flex items-center gap-2">
                {onSendToChat && (
                  <button
                    onClick={handleSendToChat}
                    className="flex items-center gap-1.5 px-3 py-1 rounded-md text-[11px] font-medium soft-text-muted hover:text-foreground hover:bg-surface-muted border soft-border transition-colors"
                  >
                    <Send className="w-3 h-3" />
                    AI'a Sor
                  </button>
                )}
                <button
                  onClick={() => setOutputOpen((v) => !v)}
                  className="soft-text-muted hover:text-foreground transition-colors p-1"
                >
                  {outputOpen ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronUp className="w-3.5 h-3.5" />}
                </button>
              </div>
            </div>

            {/* Terminal output */}
            <AnimatePresence initial={false}>
              {outputOpen && (
                <motion.div
                  initial={{ height: 0 }}
                  animate={{ height: "auto" }}
                  exit={{ height: 0 }}
                  transition={{ duration: 0.2 }}
                  className="overflow-hidden"
                >
                  <pre
                    className={`px-4 py-3 text-[13px] font-mono leading-relaxed overflow-x-auto max-h-48 overflow-y-auto whitespace-pre-wrap
                      ${success ? "text-emerald-400" : "text-amber-400"}`}
                  >
                    {stdout || stderr || "(boş çıktı)"}
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
