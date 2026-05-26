import { useState, useEffect, useRef, useCallback } from "react";
import { AudioOverviewAPI } from "@/services/api";

export interface AudioJob {
  id: string;
  status: string; // "pending" | "processing" | "ready" | "failed" etc.
  script: string;
  speakers: string[];
  errorMessage?: string;
  createdAt: string;
}

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

  // Setup isMounted tracker
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

        if (currentJob.status === "ready" || currentJob.status === "completed") {
          stopPolling();
        } else if (currentJob.status === "failed" || currentJob.errorMessage) {
          setError(currentJob.errorMessage || "Sesli özet hazırlanamadı.");
          stopPolling();
        }
      } catch (err: any) {
        if (!isMountedRef.current) return;
        if (activeJobIdRef.current !== jobId) return;
        console.error("Audio overview polling error:", err);
        setError("Sesli özet durumu sorgulanamadı.");
        stopPolling();
      }
    };

    // Poll immediately, then set interval
    void poll();
    timerRef.current = window.setInterval(poll, pollIntervalMs);
  }, [stopPolling, pollIntervalMs]);

  // Sync with initialJob if it changes
  useEffect(() => {
    if (initialJob) {
      if (isMountedRef.current) {
        setJob(initialJob);
      }
      if (initialJob.status !== "ready" && initialJob.status !== "completed" && initialJob.status !== "failed" && !initialJob.errorMessage) {
        startPolling(initialJob.id);
      } else {
        stopPolling();
      }
    } else {
      if (isMountedRef.current) {
        setJob(null);
      }
      stopPolling();
    }
  }, [initialJob, startPolling, stopPolling]);

  // Cleanup on unmount
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
