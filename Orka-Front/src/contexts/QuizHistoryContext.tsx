import { createContext, useContext, useState, useCallback, type ReactNode } from "react";
import type { QuizAttempt } from "@/lib/types";
import { QuizAPI } from "@/services/api";
import type { ApiQuizHistoryItem } from "@/lib/types";

interface QuizHistoryContextType {
  attempts: QuizAttempt[];
  addQuizAttempt: (attempt: QuizAttempt) => void;
  loadHistoryForTopic: (topicId: string) => Promise<void>;
}

const QuizHistoryContext = createContext<QuizHistoryContextType | undefined>(undefined);

export function QuizHistoryProvider({ children }: { children: ReactNode }) {
  const [attempts, setAttempts] = useState<QuizAttempt[]>([]);

  const addQuizAttempt = (attempt: QuizAttempt) => {
    setAttempts((prev) => [...prev, attempt]);
  };

  const loadHistoryForTopic = useCallback(async (topicId: string) => {
    try {
      const res = await QuizAPI.getHistory(topicId);
      const items = res.data as ApiQuizHistoryItem[];
      const mapped: QuizAttempt[] = items.map((item) => ({
        id: item.id,
        messageId: item.id,
        question: item.question,
        selectedOptionId: item.userAnswer,
        isCorrect: item.isCorrect,
        explanation: item.explanation,
        timestamp: new Date(item.createdAt),
      }));
      setAttempts(mapped);
    } catch (err) {
      console.error("Quiz history fetch error:", err);
    }
  }, []);

  return (
    <QuizHistoryContext.Provider value={{ attempts, addQuizAttempt, loadHistoryForTopic }}>
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
