// ─── UI Types ──────────────────────────────────────────────────────────────

export type MessageRole = "user" | "ai";
export type MessageType = "text" | "quiz" | "plan" | "topic_complete";

export interface QuizOption {
  id: string;
  text: string;
  isCorrect: boolean;
}

export interface QuizData {
  type?: "multiple_choice" | "coding";
  quizRunId?: string;
  questionId?: string;
  question: string;
  options: QuizOption[];
  explanation: string;
  topic?: string;
  skillTag?: string;
  topicPath?: string;
  difficulty?: string;
  cognitiveType?: string;
  sourceHint?: string;
  questionHash?: string;
  sourceRefs?: string[];
}

export interface ChatMessage {
  id: string;
  role: MessageRole;
  type: MessageType;
  content: string;
  metadata?: ChatResponseMetadata | null;
  quiz?: QuizData | QuizData[];
  completedTopicId?: string; // Set when type === "topic_complete"
  timestamp: Date;
  isStreaming?: boolean;
}

export interface CitationDto {
  citationId?: string;
  sourceType?: string;
  sourceId?: string | null;
  pageNumber?: number | null;
  label?: string | null;
  url?: string | null;
  confidence?: number | null;
}

export interface UsedToolDto {
  name?: string;
  status?: string;
  evidence?: string | null;
  fallbackReason?: string | null;
  toolId?: string | null;
  success?: boolean | null;
  fallbackUsed?: boolean | null;
  provider?: string | null;
  latencyMs?: number | null;
  citations?: CitationDto[] | null;
  sourceConfidence?: number | null;
  errorCode?: string | null;
  safeMessage?: string | null;
  groundingMode?: string | null;
  timestamp?: string | null;
}

export interface ChatResponseMetadata {
  citations?: CitationDto[];
  usedTools?: UsedToolDto[];
  groundingMode?: string;
  fallbackReason?: string | null;
  sourceConfidence?: number | null;
  providerWarnings?: string[];
  planDiagnostic?: PlanDiagnosticMeta;
}

export interface PlanDiagnosticMeta {
  planRequestId: string;
  quizRunId: string;
  topicId: string;
  topicTitle: string;
  status?: string;
  quizQuestionCount?: number;
}

export interface SubLesson {
  id: string;
  title: string;
  completed: boolean;
}

export interface WikiContent {
  topicId: string;
  subLessonId: string;
  title: string;
  content: string;
  keyPoints: string[];
  lastUpdated: Date;
}

export interface QuizAttempt {
  id: string;
  messageId: string;
  quizRunId?: string;
  questionId?: string;
  topicId?: string;
  sessionId?: string;
  question: string;
  selectedOptionId: string;
  isCorrect: boolean;
  explanation: string;
  skillTag?: string;
  topicPath?: string;
  difficulty?: string;
  cognitiveType?: string;
  questionHash?: string;
  sourceRefsJson?: string;
  timestamp: Date;
}

// ─── API Response Types ─────────────────────────────────────────────────────

/** Shape returned by GET /Topics */
export interface ApiTopic {
  id: string;
  title: string;
  emoji: string;       // backend field name
  category?: string;
  parentTopicId?: string;
  createdAt: string;
  updatedAt?: string;
  userId?: string;
  order?: number;
  subLessons?: SubLesson[];
  currentPhase?: number;
  languageLevel?: string;
  progressPercentage?: number; // %0-100 arası ilerleme
  successScore?: number;      // 0-100 arası başarı puanı
  isMastered?: boolean;       // Konu tam öğrenildi mi?
  totalSections?: number;
  completedSections?: number;
  planIntent?: string | null;
}

/** Single message returned inside a session */
export interface ApiSessionMessage {
  id: string;
  role: "User" | "AI" | "user" | "ai";
  content: string;
  messageType?: string;
  metadata?: ChatResponseMetadata | null;
  createdAt: string;
}

/** Shape returned by GET /Topics/:id/sessions/latest */
export interface ApiSession {
  sessionId: string;
  topicId: string;
  messages: ApiSessionMessage[];
}

/** Shape returned by POST /Chat/message */
export interface ApiChatResponse {
  sessionId: string;
  messageId: string;
  content: string;
  modelUsed?: string;
  messageType?: "text" | "quiz" | "plan";
  wikiUpdated?: boolean;
  wikiPageId?: string;
  planCreated?: boolean;
  isNewTopic?: boolean;
  topicTitle?: string;
  tokensUsed?: number;
  totalCostUSD?: number;
  metadata?: ChatResponseMetadata | null;
}

export interface ToolCapability {
  toolId: string;
  displayName: string;
  category: string;
  status: string;
  riskLevel: string;
  requiresAuth: boolean;
  requiresAdmin: boolean;
  requiresExternalProvider: boolean;
  configKey?: string | null;
  timeoutMs: number;
  costTracked: boolean;
  telemetryEnabled: boolean;
  fallbackMode: string;
  inputSchema: string;
  outputSchema: string;
  decision: string;
  notes: string;
}

export interface ToolCapabilitiesResponse {
  tools: ToolCapability[];
  count: number;
  includeInternal: boolean;
  contract: "tool_capability_v1" | string;
}

/** Global Stats for Dashboard */
export interface ApiGlobalStats {
  totalQuizzes: number;
  correctAnswers: number;
  accuracy: number;
  dailyProgress: Array<{
    date: string;
    total: number;
    correct: number;
    accuracy: number;
  }>;
}

/** Gamification & Learning Stats — /api/dashboard/stats */
export interface ApiDashboardStats {
  totalXP: number;
  currentStreak: number;
  completedTopics: number;
  activeLearning: number;
  totalTopics: number;
  completedSections: number;
  totalSections: number;
  progressPercentage: number;
  wikisCount: number;
  activity: Array<{ date: string; count: number }>;
  learningSignalBook?: {
    summary: string;
    totalRecentAttempts: number;
    weakSkills: Array<{
      skillTag: string;
      topicPath: string;
      wrongCount: number;
      totalCount: number;
      accuracy: number;
      lastSeenAt: string;
    }>;
    recentSignals: Array<{
      signalType: string;
      skillTag?: string;
      topicPath?: string;
      isPositive?: boolean;
      createdAt: string;
    }>;
  };
}

/** Gamification stats — /api/user/gamification */
export interface ApiGamification {
  totalXP: number;
  currentStreak: number;
  lastActiveDate: string;
  level: number;
  xpInLevel: number;
  xpToNextLevel: number;
  levelLabel: string;
}

/** Topic progress detail — /api/topics/{id}/progress */
export interface ApiTopicProgress {
  topicId: string;
  progressPercentage: number;
  quizAccuracy: number;
  completedSections: number;
  totalSections: number;
}

/** Subtopics response — /api/topics/{id}/subtopics */
export interface ApiSubtopics {
  parentId: string;
  subtopics: ApiTopic[];
  count: number;
}

/** Single quiz attempt from backend — /api/quiz/history/{topicId} */
export interface ApiQuizHistoryItem {
  id: string;
  quizRunId?: string;
  questionId?: string;
  question: string;
  userAnswer: string;
  isCorrect: boolean;
  explanation: string;
  skillTag?: string;
  topicPath?: string;
  difficulty?: string;
  cognitiveType?: string;
  questionHash?: string;
  sourceRefsJson?: string;
  createdAt: string;
}

// ─── Course Types ───────────────────────────────────────────────────────────

export type CourseLevel = "Başlangıç" | "Orta" | "İleri";
export type CourseCategory =
  | "Programlama"
  | "Veri Bilimi"
  | "Yapay Zeka"
  | "Web Geliştirme"
  | "Veritabanı";

export interface CourseLesson {
  id: string;
  title: string;
  type: "video" | "article" | "quiz" | "exercise";
  duration: string;
  completed: boolean;
}

export interface CourseModule {
  id: string;
  title: string;
  description: string;
  duration: string;
  lessons: CourseLesson[];
}

export interface Course {
  id: string;
  title: string;
  description: string;
  category: CourseCategory;
  level: CourseLevel;
  icon: string;
  totalModules: number;
  totalLessons: number;
  estimatedHours: number;
  progress: number;
  enrolled: boolean;
  modules: CourseModule[];
  tags: string[];
  instructor: string;
  rating: number;
  students: number;
}
