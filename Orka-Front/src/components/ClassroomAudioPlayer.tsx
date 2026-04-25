import { useState, useEffect, useRef, useCallback } from "react";
import { Mic, MicOff, Square } from "lucide-react";
import { storage } from "@/services/api";
import * as signalR from "@microsoft/signalr";

interface ClassroomAudioPlayerProps {
  sessionId: string;
  onInterrupt: (elapsedMs: number) => void;
  isSpeaking: boolean;
  onSpeakerActive: (speakerId: string) => void;
  onAudioEnded?: () => void;
}

export default function ClassroomAudioPlayer({ sessionId, onInterrupt, isSpeaking, onSpeakerActive, onAudioEnded }: ClassroomAudioPlayerProps) {
  const [hubConnection, setHubConnection] = useState<signalR.HubConnection | null>(null);
  const [isListening, setIsListening] = useState(false);
  const [hasJoined, setHasJoined] = useState(false);
  
  // Audio Context and Queue
  const audioCtxRef = useRef<AudioContext | null>(null);
  const nextStartTimeRef = useRef<number>(0);
  const totalElapsedMsRef = useRef<number>(0);

  const handleJoin = async () => {
    try {
      // 1. Request microphone permission explicitly
      await navigator.mediaDevices.getUserMedia({ audio: true });
      
      // 2. Initialize AudioContext within the user gesture
      if (!audioCtxRef.current) {
        audioCtxRef.current = new (window.AudioContext || (window as any).webkitAudioContext)();
        nextStartTimeRef.current = audioCtxRef.current.currentTime;
      } else if (audioCtxRef.current.state === "suspended") {
        await audioCtxRef.current.resume();
      }
      
      setHasJoined(true);
    } catch (err) {
      console.error("Mic access denied or error:", err);
      alert("Sesli sınıfa katılmak için mikrofon izni gereklidir.");
    }
  };
  
  // Connect to Hub
  useEffect(() => {
    if (!hasJoined) return;

    // Session değiştiğinde eski state'i ve referansları tamamen sıfırlıyoruz. (Memory/State Leak önlemi)
    stopAudio();
    totalElapsedMsRef.current = 0;
    
    // Yalnızca hasJoined true olduğunda AudioContext'in var olduğundan emin olalım
    if (!audioCtxRef.current) {
       audioCtxRef.current = new (window.AudioContext || (window as any).webkitAudioContext)();
       nextStartTimeRef.current = audioCtxRef.current.currentTime;
    }
    
    const token = storage.getToken();
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/classroom?access_token=${token}`)
      .withAutomaticReconnect()
      .build();

    connection.start().then(() => {
      console.log("Classroom Hub Connected.");
      connection.invoke("JoinSession", sessionId).catch(err => console.error("Kuba join error:", err));
      
      connection.on("ReceiveAudioChunk", async (payload: { base64Audio: string, speaker: string }) => {
        // Zaten hasJoined ile context'i garantiye aldık.
        if (!audioCtxRef.current) return;
        
        // Notify parent about who is speaking
        onSpeakerActive(payload.speaker);

        const audioData = Uint8Array.from(atob(payload.base64Audio), c => c.charCodeAt(0));
        await scheduleAudioChunk(audioData.buffer);
      });
      
      connection.on("OnClassroomInterrupted", () => {
         stopAudio();
      });

    }).catch(err => console.error("Classroom Hub error:", err));

    setHubConnection(connection);

    return () => {
      stopAudio();
      connection.stop();
    };
  }, [sessionId, hasJoined]);

  const activeSourcesRef = useRef<number>(0);
  const isStreamEndedRef = useRef<boolean>(!isSpeaking);

  useEffect(() => {
    isStreamEndedRef.current = !isSpeaking;
    if (!isSpeaking && activeSourcesRef.current <= 0 && onAudioEnded) {
      onAudioEnded();
    }
  }, [isSpeaking, onAudioEnded]);

  const scheduleAudioChunk = async (arrayBuffer: ArrayBuffer) => {
    const ctx = audioCtxRef.current;
    if (!ctx) return;
    
    try {
      if (ctx.state === "suspended") {
        await ctx.resume();
      }

      const audioBuffer = await ctx.decodeAudioData(arrayBuffer);
      const source = ctx.createBufferSource();
      source.buffer = audioBuffer;
      source.connect(ctx.destination);
      
      const playTime = Math.max(ctx.currentTime, nextStartTimeRef.current);
      source.start(playTime);
      activeSourcesRef.current += 1;
      
      source.onended = () => {
        activeSourcesRef.current -= 1;
        if (activeSourcesRef.current <= 0 && isStreamEndedRef.current && onAudioEnded) {
          onAudioEnded();
        }
      };
      
      nextStartTimeRef.current = playTime + audioBuffer.duration;
      totalElapsedMsRef.current += (audioBuffer.duration * 1000);
    } catch(err) {
      console.error("Audio decode error", err);
    }
  };

  const stopAudio = useCallback(() => {
     if (audioCtxRef.current) {
         audioCtxRef.current.close();
         audioCtxRef.current = null;
     }
     nextStartTimeRef.current = 0;
  }, []);

  // Walkie-Talkie Push-to-Talk Logic
  const [recognition, setRecognition] = useState<any>(null);
  const recognitionRef = useRef<any>(null);
  const transcriptRef = useRef<string>("");

  useEffect(() => {
    if (!hasJoined) return;
    
    const SpeechRecognition = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (SpeechRecognition) {
      const rec = new SpeechRecognition();
      rec.continuous = true;
      rec.interimResults = true;
      rec.lang = "tr-TR";

      rec.onresult = (event: any) => {
        let finalTranscript = "";
        for (let i = event.resultIndex; i < event.results.length; ++i) {
          if (event.results[i].isFinal) {
            finalTranscript += event.results[i][0].transcript;
          }
        }
        if (finalTranscript) {
          transcriptRef.current += finalTranscript + " ";
        }
      };

      rec.onerror = (e: any) => console.error("Speech recognition error:", e);
      setRecognition(rec);
      recognitionRef.current = rec;
    }
  }, [hasJoined]);

  const handleInterrupt = useCallback(() => {
    if (!hasJoined) return;
    
    if (!isListening) {
      setIsListening(true);
      transcriptRef.current = "";
      try {
        recognitionRef.current?.start();
      } catch(e) {} // Ignore already started error
      
      const elapsed = Math.floor(totalElapsedMsRef.current * 0.7);
      stopAudio();
      
      if (hubConnection?.state === signalR.HubConnectionState.Connected) {
          hubConnection.invoke("InterruptSession", sessionId, elapsed);
      }
      onInterrupt(elapsed);
    }
  }, [isListening, hubConnection, sessionId, onInterrupt, stopAudio, hasJoined]);

  const handleRelease = useCallback(() => {
    if (!hasJoined) return;

    if (isListening) {
      setIsListening(false);
      try {
        recognitionRef.current?.stop();
      } catch(e) {}
      
      // Allow some time for final transcript to process
      setTimeout(() => {
        const text = transcriptRef.current.trim();
        if (text && (window as any).handleVoiceDictation) {
           (window as any).handleVoiceDictation(text);
        }
        transcriptRef.current = "";
      }, 500);
    }
  }, [isListening, hasJoined]);

  useEffect(() => {
    if (!hasJoined) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.code === "Space" && !e.repeat && isSpeaking) {
        e.preventDefault();
        handleInterrupt();
      }
    };
    
    const handleKeyUp = (e: KeyboardEvent) => {
      if (e.code === "Space") {
        handleRelease();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    window.addEventListener("keyup", handleKeyUp);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      window.removeEventListener("keyup", handleKeyUp);
    };
  }, [isSpeaking, handleInterrupt, handleRelease, hasJoined]);

  if (!hasJoined) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 soft-surface border p-6 rounded-xl w-full max-w-md mx-auto soft-shadow relative overflow-hidden text-center">
         <p className="text-zinc-200 font-bold text-lg">Sesli Sınıf Hazır</p>
         <p className="text-sm text-zinc-400 mb-2">Mikrofon izni vererek otonom eğitime katılın.</p>
         <button 
           onClick={handleJoin}
           className="px-6 py-2.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-full font-semibold transition-colors flex items-center gap-2 shadow-lg"
         >
           <Mic className="w-4 h-4" />
           Sınıfa Bağlan
         </button>
      </div>
    );
  }

  return (
    <div className="flex items-center gap-4 soft-surface border p-4 rounded-xl w-full max-w-md mx-auto soft-shadow relative overflow-hidden">
      
      {/* Speaking Indicator */}
      <div className="flex flex-col flex-1 pl-2">
         {isSpeaking ? (
             <div className="flex items-center gap-2">
                <span className="flex h-2 w-2 rounded-full bg-emerald-500 animate-pulse"></span>
                <p className="text-xs font-bold text-zinc-200">Tutor Konuşuyor...</p>
             </div>
         ) : (
             <p className="text-xs text-zinc-500">Sınıf Beklemede</p>
         )}
         <p className="text-[10px] text-zinc-500 mt-1">Söz kesmek için <kbd className="bg-zinc-800 px-1.5 py-0.5 rounded text-zinc-300">Space</kbd> basılı tutun</p>
      </div>

      {/* Push to Talk Button */}
      <button 
        onMouseDown={handleInterrupt}
        onMouseUp={handleRelease}
        className={`w-14 h-14 rounded-full flex items-center justify-center transition-all shadow-lg border ${
          isListening 
            ? "bg-amber-500/10 border-amber-500/50 text-amber-700 dark:text-amber-300 scale-95" 
            : isSpeaking ? "bg-zinc-800 border-zinc-700 text-zinc-300 hover:bg-zinc-700" : "bg-zinc-800/50 border-zinc-800/50 text-zinc-600 cursor-not-allowed"
        }`}
        disabled={!isSpeaking && !isListening}
      >
        {isListening ? <Mic className="w-6 h-6 animate-pulse" /> : (isSpeaking ? <Square className="w-5 h-5" /> : <MicOff className="w-5 h-5" />)}
      </button>
    </div>
  );
}
