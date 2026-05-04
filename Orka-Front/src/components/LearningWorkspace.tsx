import type { ReactNode } from "react";
import { motion, AnimatePresence } from "framer-motion";

interface LearningWorkspaceProps {
  children: ReactNode;
  rightRail?: ReactNode;
  railOpen?: boolean;
}

export default function LearningWorkspace({ children, rightRail, railOpen = false }: LearningWorkspaceProps) {
  return (
    <div className="flex h-full min-h-0 overflow-hidden">
      <motion.div
        layout
        transition={{ duration: 0.22, ease: "easeOut" }}
        className="min-w-0 flex-1 overflow-hidden"
      >
        {children}
      </motion.div>

      {rightRail && (
        <AnimatePresence>
          {railOpen && (
            <>
              <motion.div
                layout
                initial={{ opacity: 0, width: 0, x: 24 }}
                animate={{ opacity: 1, width: "auto", x: 0 }}
                exit={{ opacity: 0, width: 0, x: 24 }}
                transition={{ duration: 0.22, ease: "easeOut" }}
                className="hidden min-h-0 shrink-0 border-l border-[#526d82]/12 bg-[#eef1f3]/70 shadow-[-18px_0_44px_rgba(66,91,112,0.07)] backdrop-blur-xl lg:flex lg:w-[38rem] 2xl:w-[44rem]"
              >
                {rightRail}
              </motion.div>

              <motion.div
                initial={{ opacity: 0, y: 28 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 28 }}
                transition={{ duration: 0.22, ease: "easeOut" }}
                className="fixed inset-x-3 bottom-3 z-50 h-[74vh] overflow-hidden rounded-[1.75rem] border border-[#526d82]/14 bg-[#eef1f3]/95 shadow-2xl backdrop-blur-2xl lg:hidden"
              >
                {rightRail}
              </motion.div>
            </>
          )}
        </AnimatePresence>
      )}
    </div>
  );
}
