import { useState, useEffect, useRef, useCallback } from "react";
import { AudioOverviewAPI } from "@/services/api";

export interface AudioJob {
  id: string;
  status: string;
  script: string;
  speakers: string[];
  surface?: string;
  contextType?: string;
  wikiPageId?: string | null;
  sourceId?: string | null;
  audioMode?: string;
  dialogueFormat?: string;
  ttsQuality?: string;
  transcript?: string;
  captionTrack?: string;
  captions?: Array<{ cueId: number; speaker: string; text: string; start: string; end: string }>;
  classroomReady?: boolean;
  crossSurfaceSync?: boolean;
  audioExpiresAt?: string | null;
  audioPurgedAt?: string | null;
  audioByteLength?: number;
  retentionNotes?: string[];
  contentType?: string | null;
  fileName?: string | null;
  downloadUrl?: string | null;
  fallbackReason?: string | null;
  errorMessage?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

const normalizeAudioStatus = (status?: string | null) =>
  (status ?? "").trim().toLowerCase().replace("_", "-");

const isTerminalAudioStatus = (status?: string | null) => {
  const normalized = normalizeAudioStatus(status);
  return normalized === "ready" ||
    normalized === "completed" ||
    normalized === "script-only" ||
    normalized === "failed";
};

const isFailedAudioStatus = (status?: string | null) =>
  normalizeAudioStatus(status) === "failed";

export function useAudioOverviewPolling(
  initialJob: AudioJob | null = null,
  pollIntervalMs = 3000
) {
  const [job, setJob] = useState<AudioJob | null>(initialJob);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const timerRef = useRef<number | null>(null);
  const activeJobIdRef = useRef<string | null>(null);
  const isMountedRef = useRef<boolean>(true);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  const stopPolling = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (isMountedRef.current) {
      setLoading(false);
    }
  }, []);

  const startPolling = useCallback((jobId: string) => {
    stopPolling();
    activeJobIdRef.current = jobId;
    if (isMountedRef.current) {
      setLoading(true);
      setError(null);
    }

    const poll = async () => {
      try {
        const currentJob = await AudioOverviewAPI.get(jobId);
        if (!isMountedRef.current) return;
        if (activeJobIdRef.current !== jobId) return;

        setJob(currentJob);

        if (isFailedAudioStatus(currentJob.status)) {
          setError(currentJob.errorMessage || "Sesli ozet hazirlanamadi.");
          stopPolling();
        } else if (isTerminalAudioStatus(currentJob.status)) {
          setError(null);
          stopPolling();
        }
      } catch (err: unknown) {
        if (!isMountedRef.current) return;
        if (activeJobIdRef.current !== jobId) return;
        console.error("Audio overview polling error:", err);
        setError("Sesli ozet durumu sorgulanamadi.");
        stopPolling();
      }
    };

    void poll();
    timerRef.current = window.setInterval(poll, pollIntervalMs);
  }, [stopPolling, pollIntervalMs]);

  useEffect(() => {
    if (initialJob) {
      if (isMountedRef.current) {
        setJob(initialJob);
      }
      if (!isTerminalAudioStatus(initialJob.status)) {
        startPolling(initialJob.id);
      } else {
        if (isFailedAudioStatus(initialJob.status)) {
          setError(initialJob.errorMessage || "Sesli ozet hazirlanamadi.");
        } else {
          setError(null);
        }
        stopPolling();
      }
    } else {
      if (isMountedRef.current) {
        setJob(null);
        setError(null);
      }
      stopPolling();
    }
  }, [initialJob, startPolling, stopPolling]);

  useEffect(() => {
    return () => {
      stopPolling();
    };
  }, [stopPolling]);

  return {
    job,
    loading,
    error,
    startPolling,
    stopPolling,
    setJob,
  };
}
