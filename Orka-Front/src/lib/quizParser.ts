import type { QuizData } from "./types";

/**
 * Backend quiz yanıtlarını parse eder.
 * Desteklenen formatlar:
 *   1. Direkt JSON string  →  { question, options:[{id,text,isCorrect}], explanation }
 *   2. Markdown code block →  ```json { ... } ```
 */
export function tryParseQuiz(content: string): QuizData | QuizData[] | null {
  const candidates: string[] = [content.trim()];

  // ```json ... ``` veya ```quiz ... ``` bloklarını dene
  const blockMatch = content.match(/```(?:json|quiz)?\s*([\s\S]+?)\s*```/i);
  if (blockMatch) candidates.unshift(blockMatch[1]);

  // fallback: süslü parantez bloklarını dene
  const firstBrace = content.indexOf('{');
  const lastBrace = content.lastIndexOf('}');
  if (firstBrace !== -1 && lastBrace !== -1 && lastBrace > firstBrace) {
      candidates.push(content.substring(firstBrace, lastBrace + 1));
  }

  for (const raw of candidates) {
    try {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed) && parsed.length > 0) {
        // Evaluate if it's an array of quizzes (coding type has empty options)
        if (
          typeof parsed[0]?.question === "string" &&
          Array.isArray(parsed[0]?.options)
        ) {
          return parsed.map((q: any, qi: number) => ({
            question: q.question,
            options: q.options.map((o: any, i: number) => ({
              id: o.id ?? `opt-${qi}-${i}`,
              text: o.text.replace(/^[A-F]\)\s*/i, "").trim(),
              isCorrect: Boolean(o.isCorrect),
            })),
            explanation: q.explanation ?? "",
            topic: q.topic,
            type: q.type === "coding" ? "coding" : "multiple_choice",
          }));
        }
      } else if (
        typeof parsed?.question === "string" &&
        Array.isArray(parsed?.options)
      ) {
        // options dizisini normalize et: isCorrect boolean garantisi
        // coding type için options boş olabilir
        const normalized: QuizData = {
          question: parsed.question,
          options: parsed.options.map(
            (o: { id?: string; text: string; isCorrect?: boolean }, i: number) => ({
              id: o.id ?? `opt-${i}`,
              text: o.text.replace(/^[A-F]\)\s*/i, "").trim(),
              isCorrect: Boolean(o.isCorrect),
            })
          ),
          explanation: parsed.explanation ?? "",
          topic: parsed.topic,
          type: parsed.type === "coding" ? "coding" : "multiple_choice",
        };
        return normalized;
      }
    } catch {
      // parse hatası → sonraki candidate'i dene
    }
  }
  return null;
}
