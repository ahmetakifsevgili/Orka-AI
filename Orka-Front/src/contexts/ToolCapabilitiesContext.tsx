import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import type { ToolCapability } from "@/lib/types";
import { ToolsAPI } from "@/services/api";

type CapabilityState = {
  tools: ToolCapability[];
  loading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
  getTool: (toolId: string) => ToolCapability | undefined;
  isEnabled: (toolId: string) => boolean;
  isVisibleForUser: (toolId: string) => boolean;
};

const ToolCapabilitiesContext = createContext<CapabilityState | null>(null);

const normalize = (value?: string | null) => (value ?? "").toLowerCase();

export function isToolEnabled(tool?: ToolCapability) {
  if (!tool) return false;
  const status = normalize(tool.status);
  return status === "enabled" || status === "ready" || status === "available";
}

export function isToolGated(tool?: ToolCapability) {
  if (!tool) return false;
  const status = normalize(tool.status);
  const decision = normalize(tool.decision);
  return (
    status.includes("beta") ||
    status.includes("gated") ||
    status.includes("disabled") ||
    status.includes("provider") ||
    decision.includes("gate") ||
    decision.includes("beta") ||
    decision.includes("stub")
  );
}

export function ToolCapabilitiesProvider({ children }: { children: ReactNode }) {
  const [tools, setTools] = useState<ToolCapability[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await ToolsAPI.getCapabilities(false);
      setTools(response.tools ?? []);
    } catch {
      setError("Araç yetenekleri yüklenemedi.");
      setTools([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const byId = useMemo(() => new Map(tools.map((tool) => [tool.toolId, tool])), [tools]);

  const value = useMemo<CapabilityState>(() => ({
    tools,
    loading,
    error,
    refresh,
    getTool: (toolId) => byId.get(toolId),
    isEnabled: (toolId) => isToolEnabled(byId.get(toolId)),
    isVisibleForUser: (toolId) => {
      const tool = byId.get(toolId);
      if (!tool) return false;
      return !tool.requiresAdmin && !normalize(tool.decision).includes("dev_only");
    },
  }), [byId, error, loading, refresh, tools]);

  return <ToolCapabilitiesContext.Provider value={value}>{children}</ToolCapabilitiesContext.Provider>;
}

export function useToolCapabilities() {
  const value = useContext(ToolCapabilitiesContext);
  if (!value) throw new Error("useToolCapabilities must be used inside ToolCapabilitiesProvider");
  return value;
}
