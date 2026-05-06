import { AlertCircle, BarChart3, Calculator, CloudSun, Code2, Film, Globe2, Sparkles } from "lucide-react";
import type { ComponentType } from "react";
import type { ToolCapability } from "@/lib/types";
import { isToolEnabled, isToolGated, useToolCapabilities } from "@/contexts/ToolCapabilitiesContext";

const DISPLAY_TOOLS = [
  "ide_execution",
  "news",
  "weather",
  "crypto",
  "wolfram_alpha",
  "youtube_pedagogy",
  "mermaid",
  "visual_generation",
];

const ICONS: Record<string, ComponentType<{ className?: string }>> = {
  ide_execution: Code2,
  news: Globe2,
  weather: CloudSun,
  crypto: BarChart3,
  wolfram_alpha: Calculator,
  youtube_pedagogy: Film,
  mermaid: Sparkles,
  visual_generation: Sparkles,
};

const LABELS: Record<string, string> = {
  ide_execution: "IDE",
  news: "News",
  weather: "Weather",
  crypto: "Crypto",
  wolfram_alpha: "Wolfram",
  youtube_pedagogy: "YouTube",
  mermaid: "Mermaid",
  visual_generation: "Visual",
};

function providerLabel(tool: ToolCapability) {
  const notes = tool.notes || "";
  if (tool.toolId === "news") return notes.includes("GDELT") ? "GDELT" : tool.requiresExternalProvider ? "provider" : "kaynak";
  if (tool.toolId === "weather") return notes.includes("Open-Meteo") ? "Open-Meteo" : "provider";
  if (tool.toolId === "crypto") return notes.includes("CoinGecko") ? "CoinGecko" : "market";
  if (tool.toolId === "wolfram_alpha") return "AppId";
  return tool.status;
}

export default function ToolCapabilityStrip({ compact = false }: { compact?: boolean }) {
  const { tools, loading, error } = useToolCapabilities();
  const visible = DISPLAY_TOOLS
    .map((id) => tools.find((tool) => tool.toolId === id))
    .filter((tool): tool is ToolCapability => {
      if (!tool) return false;
      return !tool.requiresAdmin;
    });

  if (loading) {
    return <span className="text-[10px] font-semibold text-[#8ba8b5]">Araç sözleşmesi yükleniyor...</span>;
  }

  if (error) {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full border border-amber-500/20 bg-amber-50 px-2.5 py-1 text-[10px] font-bold text-amber-700">
        <AlertCircle className="h-3 w-3" />
        Araç durumu alınamadı
      </span>
    );
  }

  return (
    <div className={`flex items-center gap-1.5 ${compact ? "max-w-[360px] overflow-hidden" : "flex-wrap"}`}>
      {visible.map((tool) => {
        const Icon = ICONS[tool.toolId] ?? Sparkles;
        const enabled = isToolEnabled(tool);
        const gated = isToolGated(tool);
        return (
          <span
            key={tool.toolId}
            title={`${tool.displayName}: ${tool.status} · ${tool.decision}${tool.notes ? ` · ${tool.notes}` : ""}`}
            className={`inline-flex items-center gap-1 rounded-full border px-2 py-1 text-[10px] font-bold ${
              enabled
                ? "border-[#9ec7d9]/45 bg-[#dcecf3]/72 text-[#2d5870]"
                : gated
                ? "border-[#e8c46f]/35 bg-[#fff8ee]/85 text-[#8a641f]"
                : "border-[#526d82]/12 bg-[#eef1f3]/70 text-[#667085]"
            }`}
          >
            <Icon className="h-3 w-3" />
            <span>{LABELS[tool.toolId] ?? tool.displayName}</span>
            {!compact && <span className="font-medium opacity-70">{providerLabel(tool)}</span>}
          </span>
        );
      })}
    </div>
  );
}
