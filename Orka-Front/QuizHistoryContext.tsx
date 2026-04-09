import { createContext, useContext, useState, type ReactNode } from "react";
import type { QuizAttempt } from "@/lib/types";

interface QuizHistoryContextType {
  attempts: QuizAttempt[];
  addQuizAttempt: (attempt: QuizAttempt) => void;
}

const QuizHistoryContext = createContext<QuizHistoryContextType | undefined>(undefined);

export function QuizHistoryProvider({ children }: { children: ReactNode }) {
  const [attempts, setAttempts] = useState<QuizAttempt[]>([]);

  const addQuizAttempt = (attempt: QuizAttempt) => {
    setAttempts((prev) => [...prev, attempt]);
  };

  return (
    <QuizHistoryContext.Provider value={{ attempts, addQuizAttempt }}>
      {children}
    </QuizHistoryContext.Provider>
  );
}

export function useQuizHistory() {
  const context = useContext(QuizHistoryContext);
  if (!context) {
    throw new Error("useQuizHistory must be used within QuizHistoryProvider");
  }
  return context;
}
