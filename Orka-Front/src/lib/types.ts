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
  assessmentItemId?: string;
  assessmentItemKey?: string;
  conceptKey?: string;
  conceptTag?: string;
  cognitiveSkill?: string;
  misconceptionTarget?: string;
  evidenceExpected?: string;
  scoringRule?: string;
  learningOutcomeIds?: string[];
  knowledgeTracingStateId?: string;
  masteryProbability?: number;
  itemQualityStatus?: string;
  sourceRefs?: unknown;
}

export interface ChatMessage {
  id: string;
  role: MessageRole;
  type: MessageType;
  content: string;
  metadata?: ChatResponseMetadata | null;
  artifacts?: TeachingArtifact[];
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
  chunkId?: string | null;
  sourceTopicId?: string | null;
  sourceTopicTitle?: string | null;
  scopeRelation?: string | null;
  retrievalScope?: string | null;
}

export interface SourceRetrievalItemDto {
  id: string;
  sourceRetrievalRunId: string;
  sourceId: string;
  sourceChunkId?: string | null;
  sourceTopicId?: string | null;
  sourceTopicTitle?: string | null;
  scopeRelation?: string | null;
  retrievalScope?: string | null;
  pageNumber: number;
  chunkIndex: number;
  rank: number;
  embeddingScore: number;
  lexicalScore: number;
  fusedScore: number;
  qualityStatus: string;
  reason?: string | null;
  snippet: string;
}

export interface SourceRetrievalRunDto {
  id: string;
  userId: string;
  topicId?: string | null;
  sessionId?: string | null;
  sourceId?: string | null;
  query: string;
  retrievalScope: string;
  requestedTopK: number;
  retrievedCount: number;
  isEmpty: boolean;
  maxScore: number;
  averageScore: number;
  qualityStatus: string;
  reason?: string | null;
  createdAt: string;
  items: SourceRetrievalItemDto[];
}

export interface SourceCitationCheckDto {
  id: string;
  sourceRetrievalRunId?: string | null;
  sourceId?: string | null;
  sourceChunkId?: string | null;
  citationId: string;
  sourceType: string;
  pageNumber?: number | null;
  chunkIndex?: number | null;
  checkStatus: string;
  confidence: number;
  reason?: string | null;
  createdAt: string;
}

export interface EvidenceQualityDto {
  status?: "strong" | "partial" | "weak" | "missing" | "unknown" | string;
  userSafeLabel?: string | null;
  reasons?: string[];
  sourceCount?: number;
  readySourceCount?: number;
  retrievedEvidenceCount?: number;
  citationCoverage?: number;
  unsupportedCitationCount?: number;
  citationMissingCount?: number;
}

export interface LearningSignalConfidenceDto {
  status?: "usable" | "observed_only" | "ignored" | string;
  confidence?: number;
  reasons?: string[];
}

export interface MisconceptionSignalDto {
  category?: string;
  userSafeLabel?: string | null;
  confidence?: number;
  confidenceStatus?: string | null;
  topicId?: string | null;
  conceptKey?: string | null;
  label?: string | null;
  safeHint?: string | null;
  evidenceBasis?: string[];
}

export interface RemediationSeedDto {
  conceptKey?: string | null;
  label?: string | null;
  topicId?: string | null;
  reason?: string | null;
  confidence?: number;
  confidenceStatus?: string | null;
  misconceptionCategory?: string | null;
  userSafeMisconceptionLabel?: string | null;
  firstAction?: "wiki_review" | "tutor_explain" | "practice_quiz" | "source_check" | "prerequisite_review" | string;
  secondaryActions?: string[];
  evidenceBasis?: string[];
}

export interface SourceQualityReportDto {
  id: string;
  userId: string;
  topicId?: string | null;
  sourceId?: string | null;
  qualityStatus: string;
  retrievalHealthStatus: string;
  citationCoverageStatus: string;
  citationSupportStatus: string;
  retrievalRunCount: number;
  emptyRunCount: number;
  citationCheckCount: number;
  unsupportedCitationCount: number;
  citationMissingCount: number;
  averageContextRelevance: number;
  citationCoverage: number;
  evidenceQuality?: EvidenceQualityDto | null;
  generatedAt: string;
  recentRetrievalRuns?: SourceRetrievalRunDto[];
  recentCitationChecks?: SourceCitationCheckDto[];
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

export interface ToolStatusDto {
  id: string;
  toolId: string;
  status: string;
  success: boolean;
  provider?: string | null;
  safeMessage?: string | null;
  errorCode?: string | null;
  confidence?: number | null;
  sourceCount?: number | null;
}

export interface ArtifactSummaryDto {
  id: string;
  artifactType: string;
  title: string;
  status: string;
  renderFormat: string;
  provider?: string | null;
  externalUrl?: string | null;
}

export interface EvidenceSummaryDto {
  readyToolCount: number;
  sourceCount: number;
  groundingStatus: string;
  learnerEvidenceStatus: string;
}

export interface TeachingArtifact {
  id: string;
  userId?: string;
  topicId?: string | null;
  sessionId?: string | null;
  tutorActionTraceId?: string | null;
  artifactType: string;
  title: string;
  content: string;
  renderFormat: string;
  status: string;
  provider?: string | null;
  externalUrl?: string | null;
  renderError?: string | null;
  metadataJson?: string | null;
  renderedAt?: string | null;
  createdAt?: string;
}

export interface ChatResponseMetadata {
  citations?: CitationDto[];
  usedTools?: UsedToolDto[];
  groundingMode?: string;
  fallbackReason?: string | null;
  sourceConfidence?: number | null;
  retrievalRunId?: string | null;
  sourceQualityStatus?: string | null;
  unsupportedCitationCount?: number;
  citationMissingCount?: number;
  providerWarnings?: string[];
  tutorPolicyTraceId?: string | null;
  tutorTurnStateId?: string | null;
  tutorWorkingMemorySnapshotId?: string | null;
  tutorActionTraceId?: string | null;
  teachingMode?: string | null;
  styleMode?: string | null;
  activeConceptKey?: string | null;
  nextPedagogicalMove?: string | null;
  groundingStatus?: string | null;
  masteryProbability?: number | null;
  confidence?: number | null;
  toolCallIds?: string[];
  artifactIds?: string[];
  toolStatuses?: ToolStatusDto[];
  artifactSummaries?: ArtifactSummaryDto[];
  evidenceSummary?: EvidenceSummaryDto | null;
  policyViolationCount?: number | null;
  ragQualityStatus?: string | null;
  evidenceQuality?: EvidenceQualityDto | null;
  tutorResponseMode?: string | null;
  evidencePolicy?: string | null;
  personalizationMode?: string | null;
  masteryBasis?: string | null;
  weakConceptHints?: string[];
  misconceptionSignal?: MisconceptionSignalDto | null;
  learningSignalConfidence?: LearningSignalConfidenceDto | null;
  remediationSeed?: RemediationSeedDto | null;
  nextCheckPrompt?: string | null;
  cognitiveLoad?: string | null;
  affectiveState?: string | null;
  tutorPedagogyEvaluationRunId?: string | null;
  tutorPedagogyStatus?: string | null;
  tutorPedagogyScore?: number | null;
  pedagogyWarnings?: string[];
  planDiagnostic?: PlanDiagnosticMeta;
}

export interface AssessmentCalibrationRun {
  id: string;
  userId: string;
  topicId?: string | null;
  conceptGraphSnapshotId?: string | null;
  calibrationStatus: string;
  adaptiveReadiness: string;
  itemBankHealth: string;
  itemCount: number;
  healthyItemCount: number;
  conceptCount: number;
  readyConceptCount: number;
  averageDifficulty: number;
  averageDiscrimination: number;
  averageExposure: number;
  createdAt: string;
  items?: Array<{
    id: string;
    assessmentItemId: string;
    conceptKey: string;
    difficultyEstimate: number;
    discriminationEstimate: number;
    exposureCount: number;
    evidenceCount: number;
    calibrationStatus: string;
    reason: string;
  }>;
}

export interface AdaptiveAssessmentSession {
  id: string;
  userId: string;
  topicId?: string | null;
  sessionId?: string | null;
  quizRunId?: string | null;
  status: string;
  targetConcepts: string[];
  stopReason: string;
  minItems: number;
  maxItems: number;
  answeredCount: number;
  correctCount: number;
  createdAt: string;
}

export interface AdaptiveAssessmentNextItem {
  sessionId: string;
  status: string;
  isComplete: boolean;
  stopReason: string;
  decision?: {
    id: string;
    assessmentItemId: string;
    conceptKey: string;
    selectionScore: number;
    masteryProbability: number;
    masteryConfidence: number;
    itemQualityScore: number;
    exposurePenalty: number;
    decisionReason: string;
    question: QuizData;
  } | null;
}

export interface TutorTraceTimeline {
  sessionId: string;
  after: string;
  lastEventId: string;
  source: string;
  traceHealth: string;
  events: TutorTraceTimelineEvent[];
}

export interface TutorTraceTimelineEvent {
  id: string;
  streamId: string;
  eventType: string;
  eventGroup: string;
  userSafeLabel: string;
  userSafeDetail: string;
  severity: string;
  values: Record<string, string>;
  occurredAt: string;
}

export interface PlanDiagnosticMeta {
  planRequestId: string;
  quizRunId: string;
  topicId: string;
  topicTitle: string;
  status?: string;
  quizQuestionCount?: number;
  conceptGraphQualityStatus?: string;
  assessmentQualityStatus?: string;
  qualityReportId?: string | null;
  intentRequestId?: string | null;
  approvedMainTopic?: string;
  approvedFocusArea?: string;
  approvedStudyGoal?: string;
  approvedResearchIntent?: string;
}

export interface StudyIntentPreview {
  intentRequestId: string;
  rawRequest: string;
  mainTopic: string;
  focusArea: string;
  studyGoal: string;
  researchIntent: string;
  confirmationText: string;
  language: string;
  clarifyingNotes: string[];
  requiresUserConfirmation: boolean;
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
  assessmentItemId?: string;
  conceptKey?: string;
  conceptTag?: string;
  cognitiveSkill?: string;
  misconceptionTarget?: string;
  evidenceExpected?: string;
  scoringRule?: string;
  learningOutcomeIdsJson?: string;
  knowledgeTracingStateId?: string;
  masteryProbability?: number;
  itemQualityStatus?: string;
  topicPath?: string;
  difficulty?: string;
  cognitiveType?: string;
  questionHash?: string;
  sourceRefsJson?: string;
  responseTimeMs?: number;
  wasSkipped?: boolean;
  confidenceSelfRating?: number;
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
  assessmentItemId?: string;
  conceptKey?: string;
  conceptTag?: string;
  cognitiveSkill?: string;
  misconceptionTarget?: string;
  evidenceExpected?: string;
  scoringRule?: string;
  learningOutcomeIdsJson?: string;
  knowledgeTracingStateId?: string;
  masteryProbability?: number;
  itemQualityStatus?: string;
  topicPath?: string;
  difficulty?: string;
  cognitiveType?: string;
  questionHash?: string;
  sourceRefsJson?: string;
  responseTimeMs?: number;
  wasSkipped?: boolean;
  confidenceSelfRating?: number;
  createdAt: string;
}

export interface LearningQualityReport {
  id: string;
  userId: string;
  topicId?: string | null;
  conceptGraphSnapshotId?: string | null;
  planRequestId?: string | null;
  qualityStatus: string;
  graphQualityStatus: string;
  assessmentQualityStatus: string;
  masteryConfidenceStatus: string;
  tutorPolicyComplianceStatus: string;
  eventHealthStatus: string;
  sourceGroundingStatus: string;
  toolExecutionHealthStatus?: string;
  artifactRenderHealthStatus?: string;
  learnerEvidenceStatus?: string;
  ragQualityStatus?: string;
  evidenceCoverageStatus?: string;
  evidenceProviderHealthStatus?: string;
  evidenceFreshnessStatus?: string;
  forumSignalUsageStatus?: string;
  evidenceCitationCoverageStatus?: string;
  tutorPedagogyStatus?: string;
  assessmentCalibrationStatus?: string;
  adaptiveReadiness?: string;
  itemBankHealth?: string;
  traceHealth?: string;
  standardsAlignmentStatus?: string;
  caseLikeCoverage?: number;
  qtiLikeCoverage?: number;
  caliperXapiCoverage?: number;
  tutorPedagogyScore?: number | null;
  criticalPedagogyViolationCount?: number;
  eventSchemaViolationCount: number;
  policyViolationCount?: number;
  recentPedagogyRubricScores?: Array<{
    rubricKey: string;
    score: number;
    severity: string;
    isCritical: boolean;
    evidence: string;
    recommendation: string;
  }>;
  generatedAt: string;
  recentToolCalls?: ToolStatusDto[];
  recentArtifacts?: TeachingArtifact[];
  recentEvidenceCards?: Array<{
    id: string;
    provider: string;
    evidenceType: string;
    title: string;
    summary: string;
    citationUrl?: string | null;
    citationLabel?: string | null;
    confidence?: number;
    riskLevel?: string;
  }>;
  latestRagEvaluation?: {
    id: string;
    qualityStatus: string;
    faithfulnessScore: number;
    contextRelevanceScore: number;
    answerRelevanceScore: number;
    citationCoverageScore: number;
    itemCount: number;
    createdAt: string;
  } | null;
  sourceQuality?: SourceQualityReportDto | null;
  assessmentCalibration?: AssessmentCalibrationRun | null;
  recentTutorTraceEvents?: TutorTraceTimelineEvent[];
  standardsSummary?: StandardsSummary | null;
  graphQuality?: {
    id: string;
    qualityStatus: string;
    conceptCount: number;
    duplicateRatio: number;
    hasPrerequisiteCycle: boolean;
    orphanConceptCount: number;
    outcomeCoverage: number;
    misconceptionCoverage: number;
    sourceEvidenceRatio: number;
    relationDensity: number;
    failures: string[];
  } | null;
  assessmentQuality?: {
    id: string;
    qualityStatus: string;
    conceptCoverage: number;
    learningOutcomeCoverage: number;
    cognitiveSkillSpread: number;
    difficultySpread: number;
    misconceptionTargetingRatio: number;
    optionQualityRatio: number;
    scoringRulePresenceRatio: number;
    failures: string[];
  } | null;
  masteryStates: Array<{
    id: string;
    conceptKey: string;
    label: string;
    evidenceCount: number;
    masteryProbability: number;
    confidence: number;
    remediationNeed: string;
    practiceReadiness: string;
  }>;
  recentTutorPolicyTraces: Array<{
    id: string;
    activeConceptKey: string;
    groundingStatus: string;
    selectedPedagogicalMove: string;
    sourceEvidenceCount: number;
    directAnswerRisk: boolean;
    policyViolations: string[];
  }>;
  resourceAlignments: Array<{
    id: string;
    sourceTitle: string;
    conceptKey: string;
    outcomeKey: string;
    alignmentScore: number;
    alignmentStatus: string;
  }>;
}

export interface StandardsSummary {
  userId: string;
  topicId?: string | null;
  conceptGraphSnapshotId?: string | null;
  standardsAlignmentStatus: string;
  caseCoverage: number;
  qtiCoverage: number;
  caliperXapiCoverage: number;
  outcomeCount: number;
  conceptCount: number;
  assessmentItemCount: number;
  learningEventCount: number;
  issueCount: number;
  generatedAt: string;
  recentIssues: StandardsValidationItem[];
}

export interface StandardsValidationRun {
  id: string;
  userId: string;
  topicId?: string | null;
  conceptGraphSnapshotId?: string | null;
  status: string;
  caseCoverage: number;
  qtiCoverage: number;
  caliperXapiCoverage: number;
  checkedItemCount: number;
  issueCount: number;
  issues: StandardsValidationItem[];
  createdAt: string;
}

export interface StandardsValidationItem {
  id: string;
  standardFamily: string;
  entityType: string;
  entityKey: string;
  severity: string;
  issueCode: string;
  userSafeMessage: string;
}

export interface StandardsExportRun {
  id: string;
  userId: string;
  topicId?: string | null;
  conceptGraphSnapshotId?: string | null;
  exportType: string;
  status: string;
  itemCount: number;
  caseCoverage: number;
  qtiCoverage: number;
  caliperXapiCoverage: number;
  payloadJson: string;
  createdAt: string;
}

export interface ProductionReadiness {
  status: string;
  sections: Array<{ key: string; status: string; userSafeLabel: string; userSafeDetail: string }>;
  providerGovernance: {
    status: string;
    providerCount: number;
    healthyProviderCount: number;
    recentFailureCount: number;
    estimatedCostUsdToday: number;
    providers: Array<{
      provider: string;
      status: string;
      calls24h: number;
      failures24h: number;
      averageLatencyMs: number;
      estimatedCostUsdToday: number;
      userSafeMessage: string;
    }>;
  };
  audioRetention: {
    status: string;
    readyAudioCount: number;
    expiredAudioCount: number;
    purgedAudioCount: number;
    storedAudioBytes: number;
    retentionDays: number;
  };
  redisStreams: {
    status: string;
    streamCount: number;
    maxLength: number;
    approximateTotalLength: number;
    trimmedStreamCount: number;
    notes: string;
  };
  dbIndexAudit: {
    status: string;
    requiredIndexCount: number;
    missingIndexCount: number;
    missingIndexes: string[];
  };
  regressionGate: {
    status: string;
    scenarios: Array<{ key: string; status: string; userSafeLabel: string; evidence: string }>;
  };
  generatedAt: string;
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
