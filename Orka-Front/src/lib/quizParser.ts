import type { QuizData } from "./types";

/**
 * Backend quiz responses are not always perfectly shaped. This parser accepts:
 * - raw JSON arrays/objects
 * - fenced ```json / ```quiz blocks
 * - wrapper objects like { questions: [...] }
 * - choices/options/answers and correctAnswer variants
 */
export function tryParseQuiz(content: string): QuizData | QuizData[] | null {
  const candidates: string[] = [];
  const trimmed = content.trim();
  if (trimmed) candidates.push(trimmed);

  for (const block of content.matchAll(/```(?:json|quiz)?\s*([\s\S]+?)\s*```/gi)) {
    if (block[1]?.trim()) candidates.unshift(block[1].trim());
  }

  collectJsonSlices(content).forEach((slice) => candidates.push(slice));

  for (const raw of Array.from(new Set(candidates))) {
    try {
      const parsed = JSON.parse(cleanJson(raw));
      const normalized = normalizeQuizPayload(parsed);
      if (normalized) return normalized;
    } catch {
      // Try next candidate.
    }
  }

  return null;
}

function cleanJson(raw: string) {
  return raw
    .trim()
    .replace(/^\uFEFF/, "")
    .replace(/,\s*([}\]])/g, "$1");
}

function collectJsonSlices(content: string): string[] {
  const slices: string[] = [];
  const starts: number[] = [];

  for (let i = 0; i < content.length; i++) {
    const ch = content[i];
    if (ch !== "{" && ch !== "[") continue;
    const next = content.slice(i + 1).match(/\S/);
    const nextChar = next?.[0];

    // Avoid speaker/citation tags like [HOCA] or [wiki].
    if ((ch === "{" && nextChar === '"') || (ch === "[" && nextChar === "{")) {
      starts.push(i);
    }
  }

  for (const start of starts) {
    const end = findMatchingJsonEnd(content, start);
    if (end > start) slices.push(content.slice(start, end + 1));
  }

  return slices;
}

function findMatchingJsonEnd(input: string, start: number) {
  const open = input[start];
  const close = open === "{" ? "}" : "]";
  const stack: string[] = [];
  let inString = false;
  let escaped = false;

  for (let i = start; i < input.length; i++) {
    const ch = input[i];

    if (inString) {
      if (escaped) escaped = false;
      else if (ch === "\\") escaped = true;
      else if (ch === '"') inString = false;
      continue;
    }

    if (ch === '"') {
      inString = true;
      continue;
    }

    if (ch === "{" || ch === "[") stack.push(ch);
    if (ch === "}" || ch === "]") {
      const expected = stack[stack.length - 1] === "{" ? "}" : "]";
      if (ch !== expected) return -1;
      stack.pop();
      if (stack.length === 0 && ch === close) return i;
    }
  }

  return -1;
}

function normalizeQuizPayload(parsed: any): QuizData | QuizData[] | null {
  const root = Array.isArray(parsed)
    ? parsed
    : parsed?.questions ?? parsed?.quizzes ?? parsed?.quiz ?? parsed?.items ?? parsed?.data ?? parsed;

  if (Array.isArray(root)) {
    const items = root
      .map((q, i) => normalizeQuestion(q, i))
      .filter(Boolean) as QuizData[];
    return items.length > 0 ? items : null;
  }

  return normalizeQuestion(root, 0);
}

function normalizeQuestion(q: any, qi: number): QuizData | null {
  const question = q?.question ?? q?.prompt ?? q?.text ?? q?.title;
  if (typeof question !== "string" || !question.trim()) return null;

  const rawOptions = q?.options ?? q?.choices ?? q?.answers ?? [];
  const isCoding = q?.type === "coding" || q?.questionType === "coding";
  if (!Array.isArray(rawOptions) && !isCoding) return null;

  const correctHint = String(
    q?.correctAnswer ?? q?.correct_answer ?? q?.answer ?? q?.correctOption ?? ""
  ).trim();

  const options = Array.isArray(rawOptions)
    ? rawOptions.map((o: any, i: number) => normalizeOption(o, i, qi, correctHint))
    : [];

  if (!isCoding && options.length === 0) return null;

  return {
    quizRunId: q?.quizRunId ?? q?.quiz_run_id,
    questionId: q?.questionId ?? q?.question_id ?? q?.id,
    question: question.trim(),
    options,
    explanation: q?.explanation ?? q?.rationale ?? q?.reason ?? "",
    topic: q?.topic,
    skillTag: q?.skillTag ?? q?.skill_tag ?? q?.skill,
    topicPath: q?.topicPath ?? q?.topic_path,
    difficulty: q?.difficulty,
    cognitiveType: q?.cognitiveType ?? q?.cognitive_type,
    sourceHint: q?.sourceHint ?? q?.source,
    questionHash: q?.questionHash ?? q?.question_hash,
    sourceRefs: q?.sourceRefs ?? q?.source_refs,
    type: isCoding ? "coding" : "multiple_choice",
  };
}

function normalizeOption(option: any, i: number, qi: number, correctHint: string) {
  const id = String(option?.id ?? option?.key ?? option?.label ?? `opt-${qi}-${i}`);
  const text = String(typeof option === "string" ? option : option?.text ?? option?.value ?? option?.label ?? "")
    .replace(/^[A-F][).]\s*/i, "")
    .trim();
  const optionLetter = String.fromCharCode(65 + i);
  const isCorrect =
    Boolean(option?.isCorrect ?? option?.is_correct ?? option?.correct) ||
    (!!correctHint && [id, optionLetter, text].some((v) => v.toLowerCase() === correctHint.toLowerCase()));

  return { id, text, isCorrect };
}
