/*
 * InteractiveIDE — Monaco tabanlı kod editörü + Piston API çalıştırıcısı.
 * Öğrenci kodu yazar → "Kodu Çalıştır" → stdout/stderr terminal blokta görünür.
 * "AI'a Sor" butonu çıktıyı TutorAgent'a context olarak gönderir.
 */

import { useState, useCallback, useRef } from "react";
import Editor from "@monaco-editor/react";
import { Play, Loader2, Send, Terminal, ChevronDown, ChevronUp, RotateCcw } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import { CodeAPI } from "@/services/api";

interface InteractiveIDEProps {
  /** Kodu çalıştırma sonucunu AI chat'e göndermek için callback */
  onSendToChat?: (message: string) => void;
  /** Aktif konu adı — editör başlığında gösterilir */
  topicTitle?: string;
}

const DEFAULT_CODE = `using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Merhaba, Orka!");
    }
}`;

const LANGUAGE_OPTIONS = [
  { value: "csharp",     label: "C#" },
  { value: "python",     label: "Python" },
  { value: "javascript", label: "JavaScript" },
  { value: "typescript", label: "TypeScript" },
  { value: "java",       label: "Java" },
  { value: "rust",       label: "Rust" },
  { value: "go",         label: "Go" },
  { value: "cpp",        label: "C++" },
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
};

export default function InteractiveIDE({ onSendToChat, topicTitle }: InteractiveIDEProps) {
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
      const result = await CodeAPI.run({ code, language });
      setStdout(result.stdout || null);
      setStderr(result.stderr || null);
      setSuccess(result.success);
      setOutputOpen(true);
    } catch {
      setStderr("Sunucuya bağlanılamadı. Lütfen tekrar deneyin.");
      setSuccess(false);
    } finally {
      setRunning(false);
    }
  }, [code, language, running]);

  const handleSendToChat = useCallback(() => {
    if (!onSendToChat) return;
    const output = stdout || stderr || "(çıktı yok)";
    const status = success ? "başarıyla çalıştı" : "hata verdi";
    const langLabel = LANGUAGE_OPTIONS.find(l => l.value === language)?.label ?? language;
    const message = [
      `Aşağıdaki ${langLabel} kodu ${status}:`,
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
        ? "Bu kodu incele ve iyileştirme önerilerinde bulun."
        : "Bu hatayı açıkla ve nasıl düzelteceğimi göster.",
    ].join("\n");

    onSendToChat(message);
  }, [code, stdout, stderr, success, language, onSendToChat]);

  const hasOutput = stdout !== null || stderr !== null;
  const langLabel = LANGUAGE_OPTIONS.find(l => l.value === language)?.label ?? language;

  return (
    <div className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden">
      {/* Header */}
      <div className="flex-shrink-0 flex items-center justify-between px-6 py-3 border-b border-zinc-800/50">
        <div className="flex items-center gap-3">
          <Terminal className="w-4 h-4 text-zinc-400" />
          <span className="text-sm font-medium text-zinc-200">
            İnteraktif Kod Editörü
          </span>
          {topicTitle && (
            <span className="text-xs text-zinc-500 pl-2 border-l border-zinc-700">
              {topicTitle}
            </span>
          )}
        </div>

        {/* Language selector */}
        <div className="flex items-center gap-3">
          <select
            value={language}
            onChange={(e) => handleLanguageChange(e.target.value)}
            className="text-xs bg-zinc-800 border border-zinc-700 text-zinc-300 rounded-lg px-3 py-1.5 focus:outline-none focus:border-zinc-500 transition-colors"
          >
            {LANGUAGE_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>

          <button
            onClick={() => { setCode(STARTER_CODE[language] ?? ""); setStdout(null); setStderr(null); setSuccess(null); }}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800 border border-zinc-700/50 transition-colors"
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
                ? "bg-zinc-800 border-zinc-700 text-zinc-500 cursor-not-allowed opacity-60"
                : "bg-emerald-900/50 border-emerald-700/60 text-emerald-300 hover:bg-emerald-800/60 hover:text-emerald-100 shadow-sm shadow-emerald-900/20"}
            `}
          >
            {running
              ? <><Loader2 className="w-3.5 h-3.5 animate-spin" /><span>Çalışıyor...</span></>
              : <><Play className="w-3.5 h-3.5" /><span>Kodu Çalıştır</span></>
            }
          </button>
        </div>
      </div>

      {/* Editor area */}
      <div className="flex-1 min-h-0 overflow-hidden">
        <Editor
          height="100%"
          defaultLanguage="csharp"
          language={language === "csharp" ? "csharp" : language}
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
            className="flex-shrink-0 border-t border-zinc-800 bg-zinc-950 overflow-hidden"
          >
            {/* Output header */}
            <div className="flex items-center justify-between px-4 py-2 border-b border-zinc-800/60">
              <div className="flex items-center gap-2">
                <div className={`w-2 h-2 rounded-full ${success ? "bg-emerald-500" : "bg-red-500"}`} />
                <span className="text-[11px] font-mono font-medium text-zinc-400 uppercase tracking-wider">
                  {success ? "Çıktı" : "Hata"} — {langLabel}
                </span>
              </div>
              <div className="flex items-center gap-2">
                {onSendToChat && (
                  <button
                    onClick={handleSendToChat}
                    className="flex items-center gap-1.5 px-3 py-1 rounded-md text-[11px] font-medium text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 border border-zinc-700/50 transition-colors"
                  >
                    <Send className="w-3 h-3" />
                    AI'a Sor
                  </button>
                )}
                <button
                  onClick={() => setOutputOpen((v) => !v)}
                  className="text-zinc-600 hover:text-zinc-400 transition-colors p-1"
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
                      ${success ? "text-emerald-400" : "text-red-400"}`}
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
