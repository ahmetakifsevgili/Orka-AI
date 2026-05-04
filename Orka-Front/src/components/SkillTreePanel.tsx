import { useMemo } from "react";
import { motion } from "framer-motion";
import { Check, Lock, Sparkles } from "lucide-react";
import type { ApiTopic } from "@/lib/types";

interface SkillTreePanelProps {
  topics: ApiTopic[];
  onFocusTopic?: (topic: ApiTopic) => void;
}

function clampPercent(value: number) {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(100, Math.round(value)));
}

function SkillNode({
  topic,
  isLocked,
  onFocusTopic,
}: {
  topic: ApiTopic;
  isLocked: boolean;
  onFocusTopic?: (t: ApiTopic) => void;
}) {
  const isMastered = topic.isMastered === true;
  const progress = clampPercent(
    topic.totalSections
      ? ((topic.completedSections ?? 0) / topic.totalSections) * 100
      : topic.progressPercentage ?? 0
  );
  const isActive = progress > 0 && !isMastered && !isLocked;

  let ringColor = "ring-[#526d82]/20";
  let bgColor = "bg-[#f4f7f7]";
  let shadow = "";

  if (isLocked) {
    ringColor = "ring-[#e1e9ea]/50";
    bgColor = "bg-[#eef1f3]/50";
  } else if (isMastered) {
    ringColor = "ring-[#547c61]/40";
    bgColor = "bg-[#ddebe3]";
    shadow = "shadow-[0_0_20px_rgba(84,124,97,0.3)]";
  } else if (isActive) {
    ringColor = "ring-[#8a641f]/40";
    bgColor = "bg-[#fff8ee]";
    shadow = "shadow-[0_0_20px_rgba(138,100,31,0.2)]";
  } else {
    ringColor = "ring-[#526d82]/30";
    bgColor = "bg-[#ffffff]";
    shadow = "shadow-sm";
  }

  return (
    <div className="relative flex flex-col items-center">
      <motion.button
        whileHover={!isLocked ? { scale: 1.05 } : {}}
        whileTap={!isLocked ? { scale: 0.95 } : {}}
        onClick={() => !isLocked && onFocusTopic?.(topic)}
        aria-disabled={isLocked}
        className={`relative z-10 flex h-20 w-20 items-center justify-center rounded-full ring-4 ring-offset-2 transition-all ${ringColor} ${bgColor} ${shadow} ${isLocked ? "cursor-not-allowed grayscale" : "cursor-pointer"}`}
      >
        {isLocked ? (
          <Lock className="h-8 w-8 text-[#8a97a0]/60" />
        ) : isMastered ? (
          <Check className="h-8 w-8 stroke-[3] text-[#547c61]" />
        ) : (
          <span className="text-3xl drop-shadow-sm">{topic.emoji || "🎯"}</span>
        )}

        {isActive && (
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-[#8a641f] opacity-20" />
        )}
      </motion.button>

      {/* Mastered badge — positioned relative to the button wrapper */}
      {isMastered && (
        <div className="absolute right-0 top-0 z-20 -translate-y-1 translate-x-1 rounded-full bg-[#fcd34d] p-1.5 shadow-md">
          <Sparkles className="h-3.5 w-3.5 text-[#8a641f]" />
        </div>
      )}

      <div className="mt-3 text-center">
        <p className={`max-w-[7rem] break-words text-sm font-extrabold leading-snug ${isLocked ? "text-[#8a97a0]" : "text-[#172033]"}`}>
          {topic.title}
        </p>
        {!isLocked && (
          <p className="mt-0.5 text-[11px] font-bold uppercase tracking-wider text-[#667085]">
            {isMastered ? "TAMAMLANDI" : progress > 0 ? `%${progress}` : "BAŞLA"}
          </p>
        )}
      </div>
    </div>
  );
}

export default function SkillTreePanel({ topics, onFocusTopic }: SkillTreePanelProps) {
  // Pre-compute tree structure once — O(n) instead of O(n×k) per render
  const { rootTopics, childrenMap } = useMemo(() => {
    const roots: ApiTopic[] = [];
    const map = new Map<string, ApiTopic[]>();

    for (const t of topics) {
      if (!t.parentTopicId) {
        roots.push(t);
      } else {
        const list = map.get(t.parentTopicId) ?? [];
        list.push(t);
        map.set(t.parentTopicId, list);
      }
    }

    roots.sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    for (const [, children] of map) {
      children.sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    }

    return { rootTopics: roots, childrenMap: map };
  }, [topics]);

  if (topics.length === 0) {
    return (
      <div className="flex min-h-[300px] items-center justify-center rounded-[2rem] border border-[#526d82]/12 bg-[#f7f9fa]/70 p-8 text-center">
        <div>
          <p className="text-sm font-extrabold text-[#172033]">Henüz Yetenek Ağacı Oluşmadı</p>
          <p className="mt-2 text-xs leading-6 text-[#667085]">
            Chat ekranından ilk hedefini belirle ve ağacını büyütmeye başla.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="relative mx-auto flex w-full max-w-4xl flex-col items-center py-10">
      <div className="relative z-10 flex w-full flex-col gap-16">
        {rootTopics.map((root, index) => {
          const children = childrenMap.get(root.id) ?? [];

          // A root is locked if the PREVIOUS root is not yet mastered
          const prevRoot = index > 0 ? rootTopics[index - 1] : null;
          const isRootLocked = prevRoot !== null && prevRoot.isMastered !== true;

          return (
            <motion.div
              key={root.id}
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: index * 0.08 }}
              className="flex w-full flex-col items-center gap-12"
            >
              {/* Root separator line (only between items) */}
              {index > 0 && (
                <div className="h-8 w-0.5 -mt-8 rounded-full bg-[#d0dde3]" />
              )}

              <SkillNode topic={root} isLocked={isRootLocked} onFocusTopic={onFocusTopic} />

              {children.length > 0 && (
                <div className="flex w-full flex-wrap justify-center gap-8 px-4 sm:gap-14">
                  {children.map((child, childIdx) => {
                    // A child is locked if its parent root hasn't been started
                    const isChildLocked =
                      isRootLocked || ((root.progressPercentage ?? 0) === 0 && root.isMastered !== true);

                    return (
                      <motion.div
                        key={child.id}
                        initial={{ opacity: 0, scale: 0.85 }}
                        animate={{ opacity: 1, scale: 1 }}
                        transition={{ delay: index * 0.08 + childIdx * 0.05 }}
                        className={childIdx % 2 !== 0 ? "mt-8" : ""}
                      >
                        <SkillNode
                          topic={child}
                          isLocked={isChildLocked}
                          onFocusTopic={onFocusTopic}
                        />
                      </motion.div>
                    );
                  })}
                </div>
              )}
            </motion.div>
          );
        })}
      </div>
    </div>
  );
}
