import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import { CheckCircle2, GitBranch, Loader2, Lock, RefreshCw, Sparkles } from "lucide-react";
import toast from "react-hot-toast";
import type { SkillEdge, SkillNode } from "@/lib/types";
import { SkillTreeAPI } from "@/services/api";

type PositionedNode = SkillNode & { x: number; y: number };

function normalizeEdge(edge: any): SkillEdge {
  return {
    source: String(edge.source ?? edge.Source ?? ""),
    target: String(edge.target ?? edge.Target ?? ""),
  };
}

function nodeTone(node: SkillNode) {
  if (node.nodeType === "Milestone") {
    return "border-amber-500/25 bg-amber-500/10 text-amber-800 dark:text-amber-200";
  }
  if (node.nodeType === "RemedialPractice") {
    return "border-emerald-500/25 bg-emerald-500/10 text-emerald-800 dark:text-emerald-200";
  }
  return "soft-border soft-surface text-foreground";
}

export default function SkillTreePanel() {
  const [nodes, setNodes] = useState<SkillNode[]>([]);
  const [edges, setEdges] = useState<SkillEdge[]>([]);
  const [loading, setLoading] = useState(true);
  const [unlockingId, setUnlockingId] = useState<string | null>(null);

  const loadTree = async () => {
    setLoading(true);
    try {
      const data = await SkillTreeAPI.get();
      setNodes(data.nodes ?? []);
      setEdges((data.edges ?? []).map(normalizeEdge));
    } catch (err) {
      console.error("Skill tree load failed", err);
      toast.error("Yetenek ağacı yüklenemedi.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadTree();
  }, []);

  const positioned = useMemo<PositionedNode[]>(() => {
    const byLevel = new Map<number, SkillNode[]>();
    nodes.forEach((node) => {
      const level = Math.max(1, Math.min(4, node.difficultyLevel || 1));
      byLevel.set(level, [...(byLevel.get(level) ?? []), node]);
    });

    return nodes.map((node) => {
      const level = Math.max(1, Math.min(4, node.difficultyLevel || 1));
      const peers = byLevel.get(level) ?? [];
      const index = peers.findIndex((peer) => peer.id === node.id);
      return {
        ...node,
        x: 90 + (level - 1) * 260,
        y: 90 + Math.max(0, index) * 132,
      };
    });
  }, [nodes]);

  const nodeById = useMemo(() => {
    const map = new Map<string, PositionedNode>();
    positioned.forEach((node) => map.set(node.id, node));
    return map;
  }, [positioned]);

  const canvasWidth = Math.max(920, ...positioned.map((node) => node.x + 240), 920);
  const canvasHeight = Math.max(520, ...positioned.map((node) => node.y + 110), 520);

  const unlockNode = async (nodeId: string) => {
    setUnlockingId(nodeId);
    try {
      await SkillTreeAPI.unlock(nodeId);
      toast.success("Düğüm açıldı.");
      await loadTree();
    } catch (err) {
      console.error("Skill node unlock failed", err);
      toast.error("Düğüm açılamadı.");
    } finally {
      setUnlockingId(null);
    }
  };

  if (loading) {
    return (
      <div className="flex-1 soft-page flex items-center justify-center">
        <Loader2 className="h-5 w-5 animate-spin soft-text-muted" />
      </div>
    );
  }

  return (
    <div className="flex-1 soft-page overflow-hidden flex flex-col">
      <header className="flex-shrink-0 soft-surface border-b soft-border px-7 py-4 flex items-center justify-between">
        <div className="min-w-0">
          <div className="flex items-center gap-2 text-[11px] uppercase tracking-wide soft-text-muted font-semibold">
            <GitBranch className="h-3.5 w-3.5" />
            Yetenek ağı
          </div>
          <h1 className="mt-1 text-lg font-semibold text-foreground">
            Öğrenme haritası
          </h1>
        </div>
        <button
          onClick={() => void loadTree()}
          className="h-9 w-9 rounded-lg border soft-border soft-muted soft-text-muted hover:text-foreground hover:bg-surface-muted flex items-center justify-center transition-colors"
          title="Yenile"
        >
          <RefreshCw className="h-4 w-4" />
        </button>
      </header>

      {nodes.length === 0 ? (
        <div className="flex-1 flex items-center justify-center px-6">
          <div className="max-w-sm text-center">
            <div className="mx-auto mb-4 h-11 w-11 rounded-xl soft-surface border soft-border flex items-center justify-center soft-shadow">
              <Sparkles className="h-5 w-5 soft-text-muted" />
            </div>
            <h2 className="text-base font-semibold text-foreground">
              Henüz düğüm yok
            </h2>
            <p className="mt-2 text-sm soft-text-muted leading-relaxed">
              Quiz ve değerlendirme akışları çalıştıkça EvaluatorAgent bu alanı
              otomatik olarak dolduracak.
            </p>
          </div>
        </div>
      ) : (
        <div className="flex-1 overflow-auto p-6">
          <div
            className="relative rounded-xl border soft-border soft-surface soft-shadow"
            style={{ width: canvasWidth, height: canvasHeight }}
          >
            <svg className="absolute inset-0 pointer-events-none" width={canvasWidth} height={canvasHeight}>
              {edges.map((edge) => {
                const source = nodeById.get(edge.source);
                const target = nodeById.get(edge.target);
                if (!source || !target) return null;
                const x1 = source.x + 210;
                const y1 = source.y + 42;
                const x2 = target.x;
                const y2 = target.y + 42;
                const mid = x1 + Math.max(36, (x2 - x1) / 2);
                return (
                  <path
                    key={`${edge.source}-${edge.target}`}
                    d={`M ${x1} ${y1} C ${mid} ${y1}, ${mid} ${y2}, ${x2} ${y2}`}
                    fill="none"
                    stroke="var(--border)"
                    strokeWidth="1.5"
                  />
                );
              })}
            </svg>

            {positioned.map((node) => (
              <motion.div
                key={node.id}
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                className={`absolute w-[210px] rounded-xl border p-3 soft-shadow ${nodeTone(node)}`}
                style={{ left: node.x, top: node.y }}
              >
                <div className="flex items-start gap-2">
                  <div className="mt-0.5">
                    {node.isUnlocked ? (
                      <CheckCircle2 className="h-4 w-4 text-emerald-500" />
                    ) : (
                      <Lock className="h-4 w-4 soft-text-muted" />
                    )}
                  </div>
                  <div className="min-w-0 flex-1">
                    <h3 className="text-sm font-semibold leading-snug truncate">
                      {node.title}
                    </h3>
                    <p className="mt-1 text-[10px] soft-text-muted uppercase tracking-wide">
                      {node.nodeType} · Seviye {node.difficultyLevel || 1}
                    </p>
                  </div>
                </div>

                {!node.isUnlocked && (
                  <button
                    onClick={() => void unlockNode(node.id)}
                    disabled={unlockingId === node.id}
                    className="mt-3 w-full rounded-lg bg-foreground text-background py-1.5 text-[11px] font-semibold hover:opacity-90 disabled:opacity-50 transition-opacity"
                  >
                    {unlockingId === node.id ? "Açılıyor..." : "Kilidi aç"}
                  </button>
                )}
              </motion.div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
