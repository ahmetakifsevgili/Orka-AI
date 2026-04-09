/*
 * Design: "Sessiz Lüks" — Functional animation only.
 * Three pulsing dots + rotating status text.
 * zinc-500 dots, opacity pulse 1.2s cycle.
 */

import { motion } from "framer-motion";

interface ThinkingIndicatorProps {
  state: string;
}

export default function ThinkingIndicator({ state }: ThinkingIndicatorProps) {
  return (
    <div className="flex items-center gap-1.5 py-2">
      {[0, 1, 2].map((i) => (
        <motion.div
          key={i}
          className="w-1.5 h-1.5 rounded-full bg-zinc-500"
          animate={{ opacity: [0.3, 1, 0.3] }}
          transition={{ duration: 1.2, repeat: Infinity, delay: i * 0.2 }}
        />
      ))}
      <span className="text-xs text-zinc-500 ml-2">{state}</span>
    </div>
  );
}
