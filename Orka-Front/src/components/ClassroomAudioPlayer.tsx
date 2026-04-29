/**
 * ClassroomAudioPlayer — Browser Web Speech API ile [HOCA]/[ASISTAN] podcast modu.
 *
 * Çalışma prensibi:
 *   - AI mesajı içeriği [HOCA]: ... ve [ASISTAN]: ... satırları içeriyorsa, her satır
 *     o role için seçilmiş bir TTS voice ile sırayla okunur (podcast diyaloğu).
 *   - Etiket yoksa düz metin tek-ses olarak okunur.
 *   - Web Speech API tarayıcı yerel TTS'i kullandığı için API key veya ücret yoktur.
 *   - Pause / Resume / Stop kontrolleri.
 */

import { useEffect, useMemo, useRef, useState } from "react";
import { Pause, Play, Send, Square, Volume2, X } from "lucide-react";
import { ClassroomAPI } from "@/services/api";

interface ClassroomAudioPlayerProps {
  text: string;
  topicId?: string;
  sessionId?: string;
  audioOverviewJobId?: string;
  onClose: () => void;
}

interface DialogueLine {
  speaker: "HOCA" | "ASISTAN" | "KONUK" | "NARRATOR";
  text: string;
}

const mojibakeByteMap = new Map<number, number>([
  [0x00c2, 0xc2], [0x00c3, 0xc3], [0x00c4, 0xc4], [0x00c5, 0xc5],
  [0x00e2, 0xe2], [0x00ef, 0xef], [0x20ac, 0x80], [0x201a, 0x82],
  [0x0192, 0x83], [0x201e, 0x84], [0x2026, 0x85], [0x2020, 0x86],
  [0x2021, 0x87], [0x02c6, 0x88], [0x2030, 0x89], [0x0160, 0x8a],
  [0x2039, 0x8b], [0x0152, 0x8c], [0x017d, 0x8e], [0x2018, 0x91],
  [0x2019, 0x92], [0x201c, 0x93], [0x201d, 0x94], [0x2022, 0x95],
  [0x2013, 0x96], [0x2014, 0x97], [0x02dc, 0x98], [0x2122, 0x99],
  [0x0161, 0x9a], [0x203a, 0x9b], [0x0153, 0x9c], [0x017e, 0x9e],
  [0x0178, 0x9f], [0x00a0, 0xa0], [0x00b0, 0xb0],
]);

const repairLikelyMojibake = (value: string): string => {
  const hasMarker = [...value].some((char) => {
    const cp = char.codePointAt(0) ?? 0;
    return cp === 0x00c2 || cp === 0x00c3 || cp === 0x00c4 || cp === 0x00c5 || cp === 0x00e2 || cp === 0x00ef;
  });
  if (!hasMarker) return value;

  const bytes: number[] = [];
  const encoder = new TextEncoder();
  for (const char of value) {
    const cp = char.codePointAt(0) ?? 0;
    if (cp <= 0x7f) bytes.push(cp);
    else if (mojibakeByteMap.has(cp)) bytes.push(mojibakeByteMap.get(cp)!);
    else bytes.push(...encoder.encode(char));
  }

  return new TextDecoder().decode(new Uint8Array(bytes));
};

const stripMarkdown = (s: string): string => {
  return s
    // kod blokları
    .replace(/```[\s\S]*?```/g, " (kod örneği) ")
    // inline kod
    .replace(/`([^`]+)`/g, "$1")
    // markdown link → metin
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    // başlıklar #
    .replace(/^#{1,6}\s+/gm, "")
    // bold/italic *
    .replace(/[*_]{1,3}([^*_]+)[*_]{1,3}/g, "$1")
    // resim
    .replace(/!\[[^\]]*\]\([^)]+\)/g, "")
    // mermaid bloğu zaten code-block'ta yok edildi
    // LaTeX
    .replace(/\$\$[\s\S]*?\$\$/g, " (formül) ")
    .replace(/\$[^$]+\$/g, " (formül) ")
    // gizli sistem etiketleri
    .replace(/\[IDE_OPEN\]/g, "")
    .replace(/\[TOPIC_COMPLETE:[^\]]+\]/g, "")
    .replace(/\[PLAN_READY\]/g, "")
    .replace(/\[VOICE_MODE:[^\]]+\]/g, "")
    .trim();
};

const parseDialogue = (raw: string): DialogueLine[] => {
  const cleaned = stripMarkdown(raw);
  const lines: DialogueLine[] = [];
  let currentSpeaker: DialogueLine["speaker"] | null = null;
  let buffer: string[] = [];

  const flush = () => {
    const text = buffer.join(" ").replace(/\s+/g, " ").trim();
    if (currentSpeaker && text) lines.push({ speaker: currentSpeaker, text });
    buffer = [];
  };

  for (const rawLine of cleaned.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line) continue;

    const match = line.match(/^\[([^\]]+)\]\s*:?\s*(.*)$/i);
    const parsedSpeaker = match ? normalizeSpeaker(match[1]) : null;
    if (match && parsedSpeaker) {
      flush();
      currentSpeaker = parsedSpeaker;
      if (match[2]) buffer.push(match[2]);
      continue;
    }

    if (currentSpeaker) {
      buffer.push(line);
    }
  }

  flush();

  if (lines.length === 0) {
    const narratorText = cleaned.replace(/\s+/g, " ").trim();
    return narratorText
      ? [
          { speaker: "HOCA", text: narratorText },
          { speaker: "ASISTAN", text: "Takıldığın yeri yazarsan hocayla birlikte orayı tekrar açalım." },
        ]
      : [];
  }

  return lines;
};

const ensureClassroomDialogue = (dialogue: DialogueLine[]): DialogueLine[] => {
  if (dialogue.length === 0) {
    return [
      { speaker: "HOCA", text: "Bu bölümü daha sade bir örnekle tekrar anlatalım." },
      { speaker: "ASISTAN", text: "Ben de öğrencinin takıldığı noktayı sorayım; en küçük adımdan başlayalım." },
    ];
  }

  const hasTeacher = dialogue.some((line) => line.speaker === "HOCA");
  const hasAssistant = dialogue.some((line) => line.speaker === "ASISTAN");
  const guarded = [...dialogue];

  if (!hasTeacher) {
    guarded.unshift({
      speaker: "HOCA",
      text: "Kısa bir çerçeve kurup bu parçayı tekrar açalım.",
    });
  }

  if (!hasAssistant) {
    guarded.push({
      speaker: "ASISTAN",
      text: "Benim takıldığım nokta şu: bu adımı küçük bir örnekle tekrar görmek gerekiyor.",
    });
  }

  return guarded;
};

const normalizeSpeaker = (speaker: string): DialogueLine["speaker"] | null => {
  const s = repairLikelyMojibake(speaker).toLocaleUpperCase("tr-TR").replace(/\s+/g, "");
  if (s === "HOCA" || s === "ÖĞRETMEN" || s === "OGRETMEN" || s === "TEACHER") return "HOCA";
  if (s === "KONUK" || s === "GUEST") return "KONUK";
  if (s === "ASISTAN" || s === "ASİSTAN" || s === "ASSISTANT") return "ASISTAN";
  return null;
};

const pickVoice = (
  voices: SpeechSynthesisVoice[],
  speaker: DialogueLine["speaker"]
): SpeechSynthesisVoice | null => {
  // Türkçe öncelik
  const trVoices = voices.filter((v) => v.lang.toLowerCase().startsWith("tr"));
  const pool = trVoices.length > 0 ? trVoices : voices;

  if (pool.length === 0) return null;

  // Erkek/kadın isim heuristic
  const maleHints = /(male|erkek|man|david|tolga|ahmet|mustafa|gunther|erik)/i;
  const femaleHints = /(female|kadın|kadin|woman|emel|zeynep|seda|ayşe|google)/i;

  if (speaker === "HOCA") {
    return (
      pool.find((v) => maleHints.test(v.name)) ||
      pool[0] ||
      voices[0]
    );
  }
  if (speaker === "ASISTAN") {
    return (
      pool.find((v) => femaleHints.test(v.name)) ||
      pool[pool.length - 1] ||
      voices[voices.length - 1]
    );
  }
  if (speaker === "KONUK") {
    return pool.find((v) => !maleHints.test(v.name) && !femaleHints.test(v.name)) || pool[0] || voices[0];
  }
  return pool[0] || voices[0];
};

export default function ClassroomAudioPlayer({
  text,
  topicId,
  sessionId,
  audioOverviewJobId,
  onClose,
}: ClassroomAudioPlayerProps) {
  const baseLines = useMemo(() => ensureClassroomDialogue(parseDialogue(text)), [text]);
  const [extraLines, setExtraLines] = useState<DialogueLine[]>([]);
  const lines = useMemo(() => [...baseLines, ...extraLines], [baseLines, extraLines]);
  const [voices, setVoices] = useState<SpeechSynthesisVoice[]>([]);
  const [currentIdx, setCurrentIdx] = useState(0);
  const activeSegment = useMemo(() => {
    if (lines.length === 0) return "";
    const active = Math.min(Math.max(currentIdx, 0), lines.length - 1);
    return lines
      .slice(Math.max(0, active - 1), Math.min(lines.length, active + 2))
      .map((line) => `[${line.speaker}]: ${line.text}`)
      .join("\n");
  }, [currentIdx, lines]);
  const [status, setStatus] = useState<"idle" | "playing" | "paused" | "done">(
    "idle"
  );
  const [question, setQuestion] = useState("");
  const [classroomId, setClassroomId] = useState<string | null>(null);
  const [isAsking, setIsAsking] = useState(false);
  const [askError, setAskError] = useState<string | null>(null);
  const utterRef = useRef<SpeechSynthesisUtterance | null>(null);
  const linesRef = useRef<DialogueLine[]>([]);
  const backendAudioRef = useRef<HTMLAudioElement | null>(null);
  const backendAudioUrlRef = useRef<string | null>(null);

  useEffect(() => {
    linesRef.current = lines;
  }, [lines]);

  useEffect(() => {
    if (!("speechSynthesis" in window)) return;
    const update = () => setVoices(window.speechSynthesis.getVoices());
    update();
    window.speechSynthesis.onvoiceschanged = update;
    return () => {
      if (backendAudioUrlRef.current) {
        URL.revokeObjectURL(backendAudioUrlRef.current);
        backendAudioUrlRef.current = null;
      }
      window.speechSynthesis.cancel();
    };
  }, []);

  const stopBackendAudio = () => {
    if (backendAudioRef.current) {
      backendAudioRef.current.pause();
      backendAudioRef.current.currentTime = 0;
      backendAudioRef.current = null;
    }
    if (backendAudioUrlRef.current) {
      URL.revokeObjectURL(backendAudioUrlRef.current);
      backendAudioUrlRef.current = null;
    }
  };

  const tryPlayBackendAudio = async (interactionId?: string): Promise<boolean> => {
    if (!interactionId) return false;

    try {
      const response = await ClassroomAPI.getInteractionAudio(interactionId);
      const audioUrl = URL.createObjectURL(response.data);
      const audio = new Audio(audioUrl);

      stopBackendAudio();
      backendAudioRef.current = audio;
      backendAudioUrlRef.current = audioUrl;

      audio.onended = () => {
        stopBackendAudio();
        setStatus("done");
      };
      audio.onerror = () => {
        stopBackendAudio();
      };

      await audio.play();
      setStatus("playing");
      return true;
    } catch {
      stopBackendAudio();
      return false;
    }
  };

  const speakLine = (idx: number, queuedLines: DialogueLine[] = linesRef.current) => {
    if (idx >= queuedLines.length) {
      setStatus("done");
      return;
    }
    const line = queuedLines[idx];
    const u = new SpeechSynthesisUtterance(line.text);
    const v = pickVoice(voices, line.speaker);
    if (v) u.voice = v;
    u.lang = v?.lang || "tr-TR";
    u.rate = line.speaker === "HOCA" ? 0.95 : 1.05;
    u.pitch = line.speaker === "HOCA" ? 0.95 : 1.1;
    u.onend = () => {
      setCurrentIdx(idx + 1);
      speakLine(idx + 1, queuedLines);
    };
    u.onerror = () => setStatus("done");
    utterRef.current = u;
    window.speechSynthesis.speak(u);
  };

  const handlePlay = () => {
    if (status === "paused") {
      if (backendAudioRef.current) {
        backendAudioRef.current.play().catch(() => undefined);
        setStatus("playing");
        return;
      }
      window.speechSynthesis.resume();
      setStatus("playing");
      return;
    }
    setStatus("playing");
    setCurrentIdx(0);
    speakLine(0, linesRef.current);
  };

  const handlePause = () => {
    if (backendAudioRef.current) {
      backendAudioRef.current.pause();
      setStatus("paused");
      return;
    }
    window.speechSynthesis.pause();
    setStatus("paused");
  };

  const handleStop = () => {
    stopBackendAudio();
    window.speechSynthesis.cancel();
    setStatus("idle");
    setCurrentIdx(0);
  };

  const handleAsk = async () => {
    const trimmed = question.trim();
    if (!trimmed || isAsking) return;

    setIsAsking(true);
    setAskError(null);
    try {
      let id = classroomId;
      if (!id) {
        const started = await ClassroomAPI.start({
          topicId,
          sessionId,
          audioOverviewJobId,
          transcript: text,
        });
        id = started.id;
        setClassroomId(id);
      }

      const answer = await ClassroomAPI.ask(id, {
        question: trimmed,
        activeSegment,
      });
      const answerLines = ensureClassroomDialogue(parseDialogue(answer.answer));
      const startIndex = linesRef.current.length;
      const queuedLines = [...linesRef.current, ...answerLines];
      linesRef.current = queuedLines;
      setExtraLines((prev) => [...prev, ...answerLines]);
      setQuestion("");
      setCurrentIdx(startIndex);

      const backendPlayed = await tryPlayBackendAudio(answer.interactionId);

      if (!backendPlayed && supports && answerLines.length > 0) {
        window.speechSynthesis.cancel();
        setStatus("playing");
        window.setTimeout(() => speakLine(startIndex, queuedLines), 0);
      }
    } catch {
      setAskError("Soru sınıf bağlamına iletilemedi; browser TTS akışı devam ediyor.");
    } finally {
      setIsAsking(false);
    }
  };

  const supports = "speechSynthesis" in window;
  const handleQuickConfusion = () => {
    const activeLine = lines[Math.min(Math.max(currentIdx, 0), Math.max(lines.length - 1, 0))];
    setQuestion(
      activeLine
        ? `Bu kısmı anlamadım: "${activeLine.text.slice(0, 160)}". Hocayla asistan daha basit bir örnekle tekrar anlatabilir mi?`
        : "Bu kısmı anlamadım. Hocayla asistan daha basit bir örnekle tekrar anlatabilir mi?"
    );
  };

  return (
    <div className="fixed bottom-6 right-6 z-50 w-[390px] max-w-[calc(100vw-2rem)] rounded-[1.5rem] bg-white/78 border border-[#526d82]/16 shadow-[0_24px_70px_rgba(66,91,112,0.22)] backdrop-blur-2xl overflow-hidden">
      <div className="flex items-center justify-between px-4 py-3 bg-[#f7fbff]/80 border-b border-[#526d82]/12">
        <div className="flex items-center gap-2">
          <Volume2 className="w-4 h-4 text-[#47725d]" />
          <span className="text-sm font-semibold text-[#172033]">Sesli Sınıf</span>
          <span className="text-[10px] font-mono px-2 py-0.5 rounded-full bg-emerald-500/10 text-[#47725d] uppercase tracking-wider">
            Podcast
          </span>
        </div>
        <button
          onClick={() => {
            handleStop();
            onClose();
          }}
          className="text-[#667085] hover:text-[#172033] transition"
          aria-label="Kapat"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      <div className="px-4 py-3 max-h-48 overflow-y-auto space-y-2 sidebar-scrollbar bg-white/45">
        {!supports && (
          <p className="text-xs text-amber-400">
            Tarayıcın Web Speech API desteklemiyor. Lütfen Chrome / Edge / Safari kullan.
          </p>
        )}
        {supports &&
          lines.map((l, i) => (
            <div
              key={i}
              className={`text-xs leading-relaxed flex gap-2 transition ${
                i === currentIdx && status === "playing"
                  ? "opacity-100"
                  : i < currentIdx
                  ? "opacity-40"
                  : "opacity-70"
              }`}
            >
              <span
                className={`flex-shrink-0 font-mono text-[10px] uppercase tracking-wider px-1.5 py-0.5 rounded ${
                  l.speaker === "HOCA"
                    ? "bg-emerald-500/15 text-[#47725d]"
                    : l.speaker === "ASISTAN"
                    ? "bg-[#fff8ee] text-[#9a6b24]"
                    : l.speaker === "KONUK"
                    ? "bg-[#dcecf3] text-[#2d5870]"
                    : "bg-white/70 text-[#667085]"
                }`}
              >
                {l.speaker === "NARRATOR" ? "ANLATICI" : l.speaker}
              </span>
              <span className="text-[#344054] line-clamp-3">{l.text}</span>
            </div>
          ))}
      </div>

      {supports && (
        <div className="px-4 py-3 border-t border-[#526d82]/12 bg-white/62 space-y-3">
          <div className="flex items-center gap-2">
          {status === "playing" ? (
            <button
              onClick={handlePause}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-white/70 hover:bg-white text-[#344054] border border-[#526d82]/12 text-xs transition"
            >
              <Pause className="w-3.5 h-3.5" /> Duraklat
            </button>
          ) : (
            <button
              onClick={handlePlay}
              disabled={voices.length === 0}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-500/15 hover:bg-emerald-500/25 text-[#47725d] text-xs transition disabled:opacity-50"
            >
              <Play className="w-3.5 h-3.5" />
              {status === "paused" ? "Devam" : status === "done" ? "Tekrar" : "Oynat"}
            </button>
          )}
          <button
            onClick={handleStop}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-white/70 hover:bg-white text-[#344054] border border-[#526d82]/12 text-xs transition"
          >
            <Square className="w-3.5 h-3.5" /> Durdur
          </button>
          <span className="ml-auto text-[10px] font-mono text-[#667085]">
            {currentIdx + (status === "done" ? 0 : 1)} / {lines.length}
          </span>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleQuickConfusion}
              className="rounded-xl border border-[#526d82]/12 bg-[#fff8ee]/80 px-3 py-2 text-xs font-semibold text-[#8a641f] transition hover:bg-[#f4ecdc]"
            >
              Anlamadım
            </button>
            <input
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") handleAsk();
              }}
              placeholder="Anlamadığın yeri hocaya sor..."
              className="flex-1 rounded-xl bg-white/72 border border-[#526d82]/15 px-3 py-2 text-xs text-[#172033] placeholder:text-[#98a2b3] outline-none focus:border-[#9ec7d9]"
            />
            <button
              onClick={handleAsk}
              disabled={!question.trim() || isAsking}
              className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-emerald-500/15 hover:bg-emerald-500/25 text-[#47725d] text-xs transition disabled:opacity-50"
            >
              <Send className="w-3.5 h-3.5" />
              Sor
            </button>
          </div>
          {askError && <p className="text-[11px] text-amber-400">{askError}</p>}
        </div>
      )}
    </div>
  );
}
