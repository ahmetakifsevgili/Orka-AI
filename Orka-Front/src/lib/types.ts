// ─── UI Types ──────────────────────────────────────────────────────────────

export type MessageRole = "user" | "ai";
export type MessageType = "text" | "quiz" | "plan" | "topic_complete";

export interface QuizOption {
  id: string;
  text: string;
}

export interface QuizData {
  type?: "multiple_choice" | "coding";
  quizRunId?: string;
  questionId?: string;
  question: string;
  options: QuizOption[];
  explanation?: string;
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
  assessmentMode?: string;
  sourceReadiness?: string;
  wikiReviewHint?: string;
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

export interface LearningMemoryTopicDto {
  topicId?: string | null;
  label: string;
  masteryProbability?: number | null;
  confidence?: number | null;
  confidenceStatus?: string | null;
  userSafeReason?: string | null;
  evidenceBasis?: string[];
}

export interface LearningMemoryConceptDto {
  topicId?: string | null;
  conceptKey?: string | null;
  label: string;
  confidence?: number | null;
  confidenceStatus?: string | null;
  userSafeReason?: string | null;
  evidenceBasis?: string[];
  remediationSeed?: RemediationSeedDto | null;
}

export interface LearningMemoryConfidenceSummaryDto {
  usableSignalCount: number;
  observedOnlySignalCount: number;
  ignoredSignalCount: number;
  strongAreaCount: number;
  weakAreaCount: number;
  userSafeSummary: string;
}

export interface GoalReadinessDto {
  observedLevel: string;
  observedLevelConfidence: number;
  plannerReadyWeakAreas: LearningMemoryConceptDto[];
  plannerReadyStrengths: LearningMemoryTopicDto[];
  plannerWarnings: string[];
  needsMoreEvidence: boolean;
  suggestedDiagnosticFocus: string[];
}

export interface LearningMemoryLiteDto {
  summary: string;
  confidenceStatus: string;
  strongTopics: LearningMemoryTopicDto[];
  weakTopics: LearningMemoryTopicDto[];
  weakConcepts: LearningMemoryConceptDto[];
  recentMisconceptions: LearningMemoryConceptDto[];
  remediationReadyItems: LearningMemoryConceptDto[];
  confidenceSummary: LearningMemoryConfidenceSummaryDto;
  sourceReadiness: string;
  recentProgressSignals: string[];
  goalReadiness: GoalReadinessDto;
  lastUpdatedAt: string;
  hasEnoughSignals: boolean;
}

export interface LearningSnapshotEvidenceSummaryDto {
  sourceEvidenceCount: number;
  wikiEvidenceCount: number;
  toolEvidenceCount: number;
  recentAttemptCount: number;
  weakConceptCount: number;
  evidenceStatus: string;
}

export interface LearningSnapshotConceptDto {
  topicId?: string | null;
  conceptKey: string;
  label: string;
  masteryProbability?: number | null;
  confidence?: number | null;
  confidenceStatus: string;
  userSafeReason: string;
  evidenceBasis: string[];
}

export interface LearningSnapshotRemediationDto {
  topicId?: string | null;
  conceptKey: string;
  label: string;
  reason: string;
  confidence?: number | null;
  confidenceStatus: string;
  firstAction: string;
  secondaryActions: string[];
  evidenceBasis: string[];
}

export interface ActiveLessonSnapshotDto {
  id: string;
  topicId?: string | null;
  sessionId?: string | null;
  planRequestId?: string | null;
  quizRunId?: string | null;
  conceptGraphSnapshotId?: string | null;
  sourceBundleHash?: string | null;
  snapshotVersion: number;
  status: string;
  activeConceptKey?: string | null;
  activeConceptLabel?: string | null;
  approvedIntent?: string | null;
  approvedMainTopic?: string | null;
  approvedFocusArea?: string | null;
  approvedStudyGoal?: string | null;
  groundingMode?: string | null;
  evidenceSummary: LearningSnapshotEvidenceSummaryDto;
  remediationNeed: string;
  learnerState: string;
  confidence?: number | null;
  masteryProbability?: number | null;
  createdAt: string;
  updatedAt: string;
  expiresAt?: string | null;
}

export interface StudentContextSnapshotDto {
  id: string;
  topicId?: string | null;
  sessionId?: string | null;
  snapshotVersion: number;
  confidenceStatus: string;
  strongConcepts: LearningSnapshotConceptDto[];
  weakConcepts: LearningSnapshotConceptDto[];
  recentMisconceptions: LearningSnapshotConceptDto[];
  remediationReady: LearningSnapshotRemediationDto[];
  reviewPressure: string[];
  sourceReadiness: string;
  goalReadiness: GoalReadinessDto;
  learningMemorySummary: string;
  createdAt: string;
  updatedAt: string;
  expiresAt?: string | null;
}

export interface ActiveLessonSnapshotRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  planRequestId?: string | null;
  quizRunId?: string | null;
  conceptGraphSnapshotId?: string | null;
  sourceBundleHash?: string | null;
  approvedIntent?: string | null;
  approvedMainTopic?: string | null;
  approvedFocusArea?: string | null;
  approvedStudyGoal?: string | null;
  groundingMode?: string | null;
}

export interface StudentContextSnapshotRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
}

export interface PlanQualityEvaluationRequestDto {
  topicId: string;
  sessionId?: string | null;
  planRequestId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  planTitle?: string | null;
  planSummary?: string | null;
  proposedSteps?: PlanStepContractDto[];
}

export interface PlanQualityEvaluationDto {
  snapshotId?: string | null;
  topicId: string;
  sessionId?: string | null;
  planRequestId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  qualityStatus: string;
  specificityScore: number;
  sequencingScore: number;
  evidenceAlignmentScore: number;
  assessmentAlignmentScore: number;
  tutorAlignmentScore: number;
  blockingIssues: PlanQualityIssueDto[];
  warningIssues: PlanQualityIssueDto[];
  planContract: PlanCurriculumSequenceDto;
  generatedAt: string;
}

export interface PlanQualityIssueDto {
  code: string;
  severity: string;
  message: string;
  stepId?: string | null;
}

export interface PlanCurriculumSequenceDto {
  topicId: string;
  topicTitle: string;
  confidenceStatus: string;
  sequenceStatus: string;
  sourceReadiness: string;
  steps: PlanStepContractDto[];
  sequencingGraph: PlanSequencingGraphDto;
  generatedAt: string;
}

export interface PlanSequencingGraphDto {
  nodes: Array<{
    conceptKey: string;
    label: string;
    order: number;
    difficultyBand: string;
  }>;
  edges: Array<{
    sourceConceptKey: string;
    targetConceptKey: string;
    relationType: string;
    weight: number;
  }>;
}

export interface PlanStepContractDto {
  stepId: string;
  title: string;
  objective: string;
  conceptKey: string;
  conceptLabel: string;
  prerequisiteConceptKeys: string[];
  targetMisconceptions: string[];
  masteryTarget: string;
  estimatedMinutes: number;
  learnerState: string;
  remediationNeed: string;
  difficultyBand: string;
  sequenceReason: string;
  evidence: PlanStepEvidenceDto;
  quizHook: PlanStepAssessmentHookDto;
  tutorHook: PlanStepTutorHookDto;
  wikiHook: PlanStepWikiHookDto;
  successCriteria: string[];
  nextStepTrigger: string;
  fallbackIfEvidenceWeak: string;
}

export interface PlanStepEvidenceDto {
  evidenceBasis: string[];
  sourceReadiness: string;
  sourceEvidenceBundleId?: string | null;
  wikiNotebookSectionKey?: string | null;
  korteksWorkflowId?: string | null;
  warnings: string[];
}

export interface PlanStepAssessmentHookDto {
  hookType: string;
  conceptKey: string;
  targetMisconceptions: string[];
  difficultyBand: string;
  userSafeReason: string;
}

export interface PlanStepTutorHookDto {
  tutorMove: string;
  activeConceptKey: string;
  targetMisconception?: string | null;
  userSafeReason: string;
}

export interface PlanStepWikiHookDto {
  sectionKey?: string | null;
  sourceReadiness: string;
  userSafeWarning?: string | null;
}

export interface PlanReadinessDto {
  topicId: string;
  topicTitle: string;
  hasConceptGraph: boolean;
  hasKorteksSynthesis: boolean;
  hasSourceEvidence: boolean;
  sourceReadiness: string;
  learnerEvidenceStatus: string;
  recommendedFirstAction: string;
  latestQualitySnapshotId?: string | null;
  warnings: string[];
}

export interface AssessmentBlueprintRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  planQualitySnapshotId?: string | null;
  planStepId?: string | null;
  assessmentMode?: string;
  conceptKey?: string | null;
  misconceptionKey?: string | null;
  itemCountTarget?: number | null;
}

export interface AssessmentBlueprintDto {
  topicId?: string | null;
  sessionId?: string | null;
  planQualitySnapshotId?: string | null;
  planStepId?: string | null;
  assessmentMode: string;
  userSafeModeLabel: string;
  targetConcepts: AssessmentBlueprintConceptDto[];
  prerequisiteConceptKeys: string[];
  misconceptionTargets: AssessmentMisconceptionTargetDto[];
  difficultyBand: string;
  itemCountTarget: number;
  cognitiveSkillMix: string[];
  evidenceMode: string;
  explanationRequirement: string;
  remediationRequirement: string;
  leakageSafetyRequirements: string[];
  warnings: string[];
}

export interface AssessmentBlueprintConceptDto {
  conceptKey: string;
  label: string;
  role: string;
  difficultyBand: string;
  confidenceStatus: string;
}

export interface AssessmentMisconceptionTargetDto {
  misconceptionKey: string;
  userSafeLabel: string;
  conceptKey: string;
  confidenceStatus: string;
  rationaleRequirement: string;
}

export interface AssessmentDistractorRationaleDto {
  optionId: string;
  rationale: string;
  misconceptionKey?: string | null;
}

export interface AssessmentItemContractDto {
  itemId: string;
  stem: string;
  conceptKey: string;
  cognitiveSkill: string;
  difficultyBand: string;
  explanation: string;
  optionTexts: string[];
  distractorRationales: AssessmentDistractorRationaleDto[];
  publicDtoContainsCorrectAnswer: boolean;
}

export interface AssessmentQualityEvaluationRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  quizRunId?: string | null;
  assessmentDraftId?: string | null;
  planQualitySnapshotId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  blueprint: AssessmentBlueprintDto;
  items: AssessmentItemContractDto[];
}

export interface AssessmentQualityEvaluationDto {
  snapshotId: string;
  topicId?: string | null;
  sessionId?: string | null;
  qualityStatus: string;
  conceptCoverageScore: number;
  misconceptionTargetingScore: number;
  distractorQualityScore: number;
  leakageSafetyScore: number;
  remediationAlignmentScore: number;
  blockingIssues: AssessmentQualityIssueDto[];
  warningIssues: AssessmentQualityIssueDto[];
  blueprint: AssessmentBlueprintDto;
  createdAt: string;
}

export interface AssessmentQualityIssueDto {
  code: string;
  severity: string;
  userSafeMessage: string;
  itemId?: string | null;
}

export interface QuizResultLearningImpactDto {
  topicId?: string | null;
  sessionId?: string | null;
  quizRunId?: string | null;
  quizAttemptId?: string | null;
  assessmentItemId?: string | null;
  assessmentMode: string;
  targetConceptKey: string;
  result: string;
  misconceptionSignal?: MisconceptionSignalDto | null;
  misconceptionConfidence: string;
  remediationNeed: string;
  masteryDelta?: number | null;
  masteryProbability?: number | null;
  nextTutorMove: string;
  nextPlanAction: string;
  wikiReviewHint?: string | null;
  sourceReadiness: string;
  evidenceBasis: string[];
}

export interface AdaptiveStudyPlanRequestDto {
  goalType: "exam" | "career" | "general_learning" | string;
  targetDate?: string | null;
  weeklyAvailableMinutes: number;
  currentLevel: string;
  examName?: string | null;
  careerTarget?: string | null;
  priorityTopicIds?: string[];
  prioritySkills?: string[];
}

export interface AdaptiveStudyPlanItemDto {
  title: string;
  reason: string;
  topicId?: string | null;
  actionType: string;
  estimatedMinutes: number;
  priority: number;
  evidenceBasis: string[];
  confidenceStatus: string;
}

export interface DiagnosticIntakeDto {
  selfDeclaredLevel: string;
  observedLevel: string;
  observedLevelConfidence: number;
  needsMoreEvidence: boolean;
  weakAreas: string[];
}

export interface DiagnosticResultDto {
  intake: DiagnosticIntakeDto;
  recommendedStartingPoint: string;
  shouldRunDiagnostic: boolean;
  userSafeReason: string;
}

export interface AdaptiveStudyPlanDto {
  summary: string;
  windowDays: number;
  items: AdaptiveStudyPlanItemDto[];
  warnings: string[];
  diagnostic: DiagnosticResultDto;
  generatedAt: string;
  hasEnoughSignals: boolean;
}

export interface SourceQualityReportDto {
  id: string;
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

export interface SourceEvidenceItemDto {
  sourceId?: string | null;
  chunkId?: string | null;
  sourceType: string;
  title: string;
  label: string;
  url?: string | null;
  pageNumber?: number | null;
  section?: string | null;
  snippetSummary: string;
  confidence: number;
  scopeRelation: string;
  retrievalScope: string;
  status: string;
  userSafeWarning?: string | null;
}

export interface SourceEvidenceBundleDto {
  id: string;
  topicId?: string | null;
  sessionId?: string | null;
  bundleHash: string;
  evidenceStatus: string;
  sourceCount: number;
  readySourceCount: number;
  chunkCount: number;
  citationCoverage: number;
  unsupportedCitationCount: number;
  staleEvidenceCount: number;
  deletedEvidenceCount: number;
  evidenceItems: SourceEvidenceItemDto[];
  warnings: string[];
  createdAt: string;
  updatedAt: string;
  expiresAt?: string | null;
}

export interface SourceEvidenceBundleRequestDto {
  sessionId?: string | null;
  question?: string | null;
}

export interface SourceLifecycleSummaryDto {
  topicId: string;
  sourceCount: number;
  readySourceCount: number;
  staleSourceCount: number;
  deletedSourceCount: number;
  failedSourceCount: number;
  activeChunkCount: number;
  evidenceStatus: string;
  warnings: string[];
}

export interface MarkSourceStaleRequestDto {
  reason?: string | null;
}

export interface ValidateSourceCitationDto {
  citationId: string;
  sourceId?: string | null;
  chunkId?: string | null;
  pageNumber?: number | null;
  chunkIndex?: number | null;
}

export interface ValidateSourceCitationSetRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  citations: ValidateSourceCitationDto[];
}

export interface SourceCitationValidationResultDto {
  citationId: string;
  supported: boolean;
  status: string;
  sourceType: string;
  userSafeWarning?: string | null;
  sourceId?: string | null;
  chunkId?: string | null;
  pageNumber?: number | null;
}

export interface SourceCitationSetValidationDto {
  totalCount: number;
  supportedCount: number;
  unsupportedCount: number;
  results: SourceCitationValidationResultDto[];
}

export interface WikiNotebookSectionDto {
  sectionKey: string;
  title: string;
  conceptKey?: string | null;
  evidenceItems: SourceEvidenceItemDto[];
  wikiBlockIds: string[];
  sourceIds: string[];
  status: string;
}

export interface WikiKnowledgeNotebookDto {
  topicId: string;
  title: string;
  evidenceStatus: string;
  sourceCoverage: string;
  conceptCoverage: string;
  sections: WikiNotebookSectionDto[];
  sourceWarnings: string[];
  lastUpdatedAt: string;
}

export interface SourceNotebookSourceDto {
  id: string;
  topicId?: string | null;
  sessionId?: string | null;
  title: string;
  fileName: string;
  status: string;
  sourceReadiness: string;
  evidenceStatus: string;
  pageCount: number;
  chunkCount: number;
  citationCoverage: number;
  linkedWikiPageId?: string | null;
  linkedWikiPageTitle?: string | null;
  latestPackId?: string | null;
  warnings: string[];
  createdAt: string;
  updatedAt: string;
}

export interface SourceNotebookWikiPageDto {
  id: string;
  title: string;
  pageKey: string;
  pageType: string;
  sourceReadiness: string;
  evidenceStatus: string;
}

export interface SourceNotebookPackRefDto {
  id: string;
  packType: string;
  packStatus: string;
  title: string;
  sourceId?: string | null;
  wikiPageId?: string | null;
  sourceReadiness: string;
  evidenceStatus: string;
  updatedAt: string;
}

export interface SourceNotebookDto {
  topicId?: string | null;
  sourceId?: string | null;
  surface: string;
  title: string;
  sourceReadiness: string;
  evidenceStatus: string;
  sourceCount: number;
  readySourceCount: number;
  chunkCount: number;
  citationCoverage: number;
  warnings: string[];
  sources: SourceNotebookSourceDto[];
  linkedWikiPages: SourceNotebookWikiPageDto[];
  packs: SourceNotebookPackRefDto[];
  nextActions: NotebookStudioNextActionDto[];
  generatedAt: string;
}

export interface SourceConceptLinkDto {
  sourceId?: string | null;
  sourceTitle: string;
  sourcePageId?: string | null;
  conceptKey: string;
  conceptTitle: string;
  wikiPageId?: string | null;
  linkType: string;
  confidence: "high" | "medium" | "low" | string;
  confidenceScore?: number | null;
  basis: string;
  evidenceStatus: string;
  sourceReadiness: string;
  isSuggestion: boolean;
  warnings: string[];
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface SourceConceptLinkSummaryDto {
  topicId?: string | null;
  sourceId?: string | null;
  wikiPageId?: string | null;
  title: string;
  sourceReadiness: string;
  evidenceStatus: string;
  confirmedLinkCount: number;
  suggestedLinkCount: number;
  links: SourceConceptLinkDto[];
  warnings: string[];
  generatedAt: string;
}

export interface SourceConceptGraphNodeDto {
  id: string;
  nodeType: "source_page" | "concept_page" | string;
  label: string;
  sourceId?: string | null;
  wikiPageId?: string | null;
  conceptKey?: string | null;
  status: string;
  sourceReadiness: string;
  evidenceStatus: string;
}

export interface SourceConceptGraphEdgeDto {
  sourceNodeId: string;
  targetNodeId: string;
  linkType: string;
  confidence: "high" | "medium" | "low" | string;
  confidenceScore?: number | null;
  basis: string;
  isSuggestion: boolean;
  warnings: string[];
}

export interface SourceConceptGraphDto {
  topicId: string;
  graphStatus: string;
  nodes: SourceConceptGraphNodeDto[];
  edges: SourceConceptGraphEdgeDto[];
  warnings: string[];
  generatedAt: string;
}

export interface SourceQuestionRequestDto {
  topicId?: string | null;
  sourceId?: string | null;
  sourceIds?: string[];
  wikiPageId?: string | null;
  notebookPackId?: string | null;
  question: string;
  mode?: "selected_source" | "source_collection" | "wiki_page_sources" | "linked_concept_sources";
  includeLearnerContext?: boolean;
  writeWikiTrace?: boolean;
}

export interface SourceQuestionCitationDto {
  citationId: string;
  sourceId?: string | null;
  sourceChunkId?: string | null;
  pageNumber?: number | null;
  chunkIndex?: number | null;
  label: string;
  sourceTitle: string;
  supportStatus: string;
  confidence?: number | null;
}

export interface SourceQuestionSafetyDto {
  status: string;
  blockedTerms: string[];
  rawPayloadRemoved: boolean;
}

export interface SourceQuestionContextDto {
  sourceId?: string | null;
  sourceTitle?: string | null;
  topicId?: string | null;
  wikiPageId?: string | null;
  wikiPageTitle?: string | null;
  relatedConcepts: SourceConceptLinkDto[];
  relatedWikiPages: SourceConceptLinkDto[];
}

export interface SourceQuestionResponseDto {
  answer: string;
  sourceBasis: string;
  evidenceStatus: string;
  sourceReadiness: string;
  citations: SourceQuestionCitationDto[];
  relatedConcepts: SourceConceptLinkDto[];
  relatedWikiPages: SourceConceptLinkDto[];
  warnings: string[];
  safety: SourceQuestionSafetyDto;
  context: SourceQuestionContextDto;
  traceBlockId?: string | null;
  nextActions: string[];
}

export interface SourceQuestionThreadRequestDto {
  topicId?: string | null;
  sourceId?: string | null;
  sourceIds?: string[];
  wikiPageId?: string | null;
  conceptKey?: string | null;
  title?: string | null;
  initialQuestion?: string | null;
  mode?: string;
  includeLearnerContext?: boolean;
  writeWikiTrace?: boolean;
}

export interface SourceQuestionFollowUpRequestDto {
  question: string;
  includeLearnerContext?: boolean;
  writeWikiTrace?: boolean;
}

export interface SourceQuestionReviewStateDto {
  turnId?: string | null;
  reviewStatus: string;
  warnings?: string[];
}

export interface SourceQuestionTurnDto {
  turnId: string;
  question: string;
  safeAnswerSummary: string;
  sourceBasis: string;
  evidenceStatus: string;
  citations: SourceQuestionCitationDto[];
  relatedConcepts: SourceConceptLinkDto[];
  relatedWikiPages: SourceConceptLinkDto[];
  reviewStatus: string;
  warnings: string[];
  traceBlockId?: string | null;
  createdAt: string;
}

export interface SourceQuestionThreadDto {
  threadId: string;
  topicId?: string | null;
  sourceIds: string[];
  wikiPageId?: string | null;
  conceptKey?: string | null;
  title: string;
  status: string;
  sourceBasis: string;
  evidenceStatus: string;
  sourceReadiness: string;
  citationReviewStatus: string;
  linkedConcepts: SourceConceptLinkDto[];
  linkedWikiPages: SourceConceptLinkDto[];
  warnings: string[];
  turns: SourceQuestionTurnDto[];
  createdAt: string;
  updatedAt: string;
}

export interface SourceQuestionThreadListDto {
  count: number;
  items: SourceQuestionThreadDto[];
}

export interface SourceQuestionMemorySummaryDto {
  threadCount: number;
  turnCount: number;
  needsReviewCount: number;
  degradedCount: number;
  recentQuestions: string[];
  warnings: string[];
}

export interface SourceStudySummaryDto {
  topicId?: string | null;
  sourceId?: string | null;
  wikiPageId?: string | null;
  sourceCount: number;
  threadCount: number;
  turnCount: number;
  reviewedCount: number;
  needsReviewCount: number;
  degradedCount: number;
  citationWarningCount: number;
  relatedConceptCount: number;
  comparedSourceCount: number;
  sourceReadiness: string;
  evidenceStatus: string;
  studyStatus: string;
  recommendedNextAction: string;
  nextActions: string[];
  recentQuestions: string[];
  warnings: string[];
  generatedAt: string;
}

export interface MultiSourceCompareRequestDto {
  topicId?: string | null;
  sourceIds: string[];
  wikiPageId?: string | null;
  conceptKey?: string | null;
  includeConceptLinks?: boolean;
  includeCitationReview?: boolean;
  writeWikiTrace?: boolean;
}

export interface MultiSourceCompareSourceDto {
  sourceId: string;
  sourceTitle: string;
  status: string;
  sourceReadiness: string;
  evidenceStatus: string;
  pageCount: number;
  chunkCount: number;
  citationCoverage: number;
  citationCheckCount: number;
  supportedCitationCount: number;
  unsupportedCitationCount: number;
  missingCitationCount: number;
  needsReviewCitationCount: number;
  linkedConceptCount: number;
  warnings: string[];
}

export interface MultiSourceConceptOverlapDto {
  conceptKey: string;
  conceptTitle: string;
  wikiPageId?: string | null;
  sourceIds: string[];
  sourceTitles: string[];
  linkConfidence: "high" | "medium" | "low" | string;
  isSuggestion: boolean;
  basis: string;
  warnings: string[];
}

export interface MultiSourceCitationCoverageDto {
  totalCitationChecks: number;
  supportedCount: number;
  unsupportedCount: number;
  missingCount: number;
  staleCount: number;
  needsReviewCount: number;
  coverageRatio: number;
  coverageStatus: string;
}

export interface CitationReviewItemDto {
  id: string;
  citationId: string;
  sourceId?: string | null;
  sourceTitle: string;
  sourceChunkId?: string | null;
  pageNumber?: number | null;
  chunkIndex?: number | null;
  sourceReadiness: string;
  evidenceStatus: string;
  citationStatus: string;
  confidence?: number | null;
  userSafeWarning: string;
  createdAt: string;
}

export interface CitationReviewResultDto {
  topicId?: string | null;
  sourceId?: string | null;
  reviewStatus: string;
  coverage: MultiSourceCitationCoverageDto;
  items: CitationReviewItemDto[];
  warnings: string[];
  generatedAt: string;
}

export interface MultiSourceCompareResultDto {
  topicId?: string | null;
  comparedSourceCount: number;
  compareStatus: string;
  evidenceStatus: string;
  sourceReadiness: string;
  sourceSummaries: MultiSourceCompareSourceDto[];
  sharedConcepts: MultiSourceConceptOverlapDto[];
  sourceOnlyConcepts: MultiSourceConceptOverlapDto[];
  citationCoverage: MultiSourceCitationCoverageDto;
  citationReviewItems: CitationReviewItemDto[];
  warnings: string[];
  nextActions: string[];
  traceBlockId?: string | null;
  safetyStatus: string;
  generatedAt: string;
}

export interface WikiGraphPageDto {
  id: string;
  topicId: string;
  parentWikiPageId?: string | null;
  pageKey: string;
  pageType: string;
  conceptKey?: string | null;
  parentConceptKey?: string | null;
  title: string;
  status: string;
  sourceReadiness: string;
  evidenceStatus: string;
  safeSummary?: string | null;
  orderIndex: number;
  blockCount: number;
  updatedAt: string;
}

export interface WikiGraphLinkDto {
  id: string;
  sourcePageId: string;
  targetPageId?: string | null;
  targetPageKey: string;
  linkType: string;
  strength: number;
  createdBy: string;
  safeLabel: string;
  createdAt: string;
}

export interface WikiGraphDto {
  topicId: string;
  focusPageId?: string | null;
  graphStatus: string;
  pages: WikiGraphPageDto[];
  links: WikiGraphLinkDto[];
  warnings: string[];
  generatedAt: string;
}

export interface WikiGraphSyncRequestDto {
  conceptGraphSnapshotId?: string | null;
  includeTopicTreeFallback?: boolean;
  createSummaryBlocks?: boolean;
}

export interface WikiGraphSyncResultDto {
  topicId: string;
  conceptGraphSnapshotId?: string | null;
  syncStatus: string;
  sourceReadiness: string;
  evidenceStatus: string;
  createdPageCount: number;
  updatedPageCount: number;
  createdLinkCount: number;
  warnings: string[];
  graph: WikiGraphDto;
}

export interface CreateWikiBlockRequestDto {
  blockType?: string;
  title?: string | null;
  content: string;
  sourceBasis?: string;
  source?: string | null;
  conceptKey?: string | null;
  misconceptionKey?: string | null;
  quizAttemptId?: string | null;
  sourceEvidenceBundleId?: string | null;
  learningArtifactId?: string | null;
  tutorTurnStateId?: string | null;
  visibility?: string;
}

export interface WikiBlockDto {
  id: string;
  wikiPageId: string;
  blockType: string;
  title: string;
  content: string;
  source?: string | null;
  sourceBasis: string;
  conceptKey?: string | null;
  misconceptionKey?: string | null;
  quizAttemptId?: string | null;
  sourceEvidenceBundleId?: string | null;
  learningArtifactId?: string | null;
  tutorTurnStateId?: string | null;
  visibility: string;
  safetyWarnings: string[];
  orderIndex: number;
  createdAt: string;
  updatedAt: string;
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

export interface LearningArtifactSafetyDto {
  status: string;
  warnings: string[];
  blockingIssues: string[];
}

export interface LearningArtifactAccessibilityDto {
  status: string;
  altText?: string | null;
  caption?: string | null;
  summary?: string | null;
  textFallback?: string | null;
  language?: string | null;
  issues: string[];
}

export interface LearningArtifactDto {
  id: string;
  topicId?: string | null;
  sessionId?: string | null;
  tutorTurnStateId?: string | null;
  tutorActionTraceId?: string | null;
  teachingArtifactId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  planQualitySnapshotId?: string | null;
  assessmentQualitySnapshotId?: string | null;
  sourceEvidenceBundleId?: string | null;
  wikiNotebookSectionKey?: string | null;
  conceptKey?: string | null;
  conceptLabel?: string | null;
  artifactType: string;
  artifactStatus: string;
  origin: string;
  renderFormat: string;
  title: string;
  safeContent: string;
  contentJson?: string | null;
  sourceBasis: string;
  citationIds: string[];
  toolTraceIds: string[];
  accessibility: LearningArtifactAccessibilityDto;
  safety: LearningArtifactSafetyDto;
  createdAt: string;
  updatedAt: string;
}

export interface LearningArtifactRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  tutorTurnStateId?: string | null;
  tutorActionTraceId?: string | null;
  teachingArtifactId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  planQualitySnapshotId?: string | null;
  assessmentQualitySnapshotId?: string | null;
  sourceEvidenceBundleId?: string | null;
  wikiNotebookSectionKey?: string | null;
  conceptKey?: string | null;
  conceptLabel?: string | null;
  artifactType: string;
  artifactStatus?: string;
  origin?: string;
  renderFormat?: string;
  title: string;
  safeContent: string;
  contentJson?: string | null;
  sourceBasis?: string;
  citationIds?: string[];
  toolTraceIds?: string[];
  accessibility?: Partial<LearningArtifactAccessibilityDto>;
}

export interface LearningArtifactListDto {
  items: LearningArtifactDto[];
  count: number;
}

export interface LearningArtifactRefreshRequestDto {
  reason?: string | null;
}

export interface NotebookStudioNextActionDto {
  actionType: string;
  userSafeLabel: string;
  priority: string;
}

export interface LearningNotebookPackDto {
  id: string;
  topicId: string;
  sessionId?: string | null;
  wikiPageId?: string | null;
  wikiPageTitle?: string | null;
  wikiPageKey?: string | null;
  sourceSurface?: string | null;
  sourceId?: string | null;
  sourceTitle?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  sourceEvidenceBundleId?: string | null;
  wikiNotebookSnapshotId?: string | null;
  planQualitySnapshotId?: string | null;
  assessmentQualitySnapshotId?: string | null;
  packType: string;
  packStatus: string;
  title: string;
  summary: string;
  sourceReadiness: string;
  evidenceStatus: string;
  completedConceptKeys: string[];
  weakConceptKeys: string[];
  misconceptionKeys: string[];
  artifactIds: string[];
  artifacts: LearningArtifactDto[];
  nextActions: NotebookStudioNextActionDto[];
  warnings: string[];
  createdAt: string;
  updatedAt: string;
}

export interface LearningNotebookPackListDto {
  count: number;
  items: LearningNotebookPackDto[];
}

export interface LearningNotebookPackRequestDto {
  sessionId?: string | null;
  wikiPageId?: string | null;
  sourceId?: string | null;
  sourceSurface?: string | null;
  packType?: string;
  focusConceptKey?: string | null;
  userGoal?: string | null;
  includeArtifacts?: boolean;
}

export interface LearningNotebookArtifactRequestDto {
  artifactType: string;
  conceptKey?: string | null;
  wikiPageId?: string | null;
}

export interface NotebookExportRequestDto {
  format?: "slide_preview" | "markdown" | "html" | "manifest_only" | "pptx_local_proof" | string;
  slideDeckArtifactId?: string | null;
}

export interface NotebookSlideExportItemDto {
  order: number;
  slideId: string;
  title: string;
  bullets: string[];
  hasSpeakerNotes: boolean;
  speakerNotes?: string | null;
  sourceLabel?: string | null;
  visualSuggestion?: string | null;
  checkpointQuestion?: string | null;
  misconceptionWarning?: string | null;
  accessibilitySummary: string;
}

export interface NotebookSlideExportPreviewDto {
  packId: string;
  slideDeckArtifactId?: string | null;
  deckTitle: string;
  slideCount: number;
  sourceBasis: string;
  sourceReadiness: string;
  exportReadiness: string;
  slides: NotebookSlideExportItemDto[];
  warnings: string[];
  accessibilitySummary: string;
  generatedAt: string;
}

export interface NotebookExportSafetyDto {
  status: string;
  warnings: string[];
  blockingIssues: string[];
}

export interface NotebookExportAccessibilityDto {
  status: string;
  summary: string;
  hasSpeakerNotes: boolean;
  hasCheckpointQuestions: boolean;
  hasTextFallback: boolean;
  issues: string[];
}

export interface NotebookExportResultDto {
  packId: string;
  slideDeckArtifactId?: string | null;
  format: string;
  status: string;
  exportReadiness: string;
  title: string;
  sourceBasis: string;
  sourceReadiness: string;
  content: string;
  contentType: string;
  fileName?: string | null;
  binaryExportAvailable: boolean;
  pptxLocalProofAvailable: boolean;
  preview: NotebookSlideExportPreviewDto;
  safety: NotebookExportSafetyDto;
  accessibility: NotebookExportAccessibilityDto;
  warnings: string[];
  createdAt: string;
}

export interface LearningWorkspaceCurrentPlanStep {
  id?: string | null;
  title?: string | null;
  objective?: string | null;
  conceptKey?: string | null;
  conceptLabel?: string | null;
  sequenceReason?: string | null;
  tutorMove?: string | null;
  quizHook?: string | null;
  sourceReadiness?: string | null;
  fallbackIfEvidenceWeak?: string | null;
}

export interface LearningWorkspaceState {
  topicId?: string | null;
  sessionId?: string | null;
  activeLessonSnapshot?: ActiveLessonSnapshotDto | null;
  studentContextSnapshot?: StudentContextSnapshotDto | null;
  currentPlanStep?: LearningWorkspaceCurrentPlanStep | null;
  planQuality?: PlanQualityEvaluationDto | null;
  planReadiness?: PlanReadinessDto | null;
  tutorPolicy?: TutorResponsePolicyDto | null;
  latestAssessmentImpact?: QuizResultLearningImpactDto | null;
  sourceReadiness?: string | null;
  sourceEvidenceBundle?: SourceEvidenceBundleDto | null;
  wikiNotebookStatus?: WikiKnowledgeNotebookDto | null;
  toolGovernanceSummary?: ToolGovernanceSummary | null;
  runtimeHealth?: LearningRuntimeHealthDto | null;
  notebookPacks?: LearningNotebookPackDto[];
  recentArtifacts: LearningArtifactDto[];
  nextActions: TutorNextLearningActionDto[];
  staleWarnings: string[];
  safetyWarnings: string[];
  isLoading: boolean;
  lastSyncedAt?: string | null;
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
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  planQualitySnapshotId?: string | null;
  tutorActionTraceId?: string | null;
  teachingMode?: string | null;
  styleMode?: string | null;
  activeConceptKey?: string | null;
  nextPedagogicalMove?: string | null;
  groundingStatus?: string | null;
  masteryProbability?: number | null;
  confidence?: number | null;
  lessonSnapshotStatus?: string | null;
  studentContextConfidenceStatus?: string | null;
  currentPlanStepId?: string | null;
  currentPlanStepTitle?: string | null;
  currentPlanTutorMove?: string | null;
  currentPlanQuizHook?: string | null;
  planSourceReadiness?: string | null;
  toolCallIds?: string[];
  artifactIds?: string[];
  toolStatuses?: ToolStatusDto[];
  artifactSummaries?: ArtifactSummaryDto[];
  evidenceSummary?: EvidenceSummaryDto | null;
  policyViolationCount?: number | null;
  ragQualityStatus?: string | null;
  evidenceQuality?: EvidenceQualityDto | null;
  tutorResponseMode?: string | null;
  tutorTeachingMove?: string | null;
  tutorResponseDepth?: string | null;
  tutorGroundingPolicy?: string | null;
  tutorRemediationPolicy?: string | null;
  tutorToolPolicy?: string | null;
  tutorNextLearningActions?: string[];
  tutorContextUse?: string[];
  tutorResponseQualityStatus?: string | null;
  tutorResponseQualityWarnings?: string[];
  activePlanStepId?: string | null;
  latestAssessmentMode?: string | null;
  latestMisconceptionConfidence?: string | null;
  sourceReadiness?: string | null;
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

export interface TutorContextUseDto {
  contextType: string;
  status: string;
  userSafeSummary?: string | null;
}

export interface TutorNextLearningActionDto {
  actionType: string;
  userSafeLabel: string;
  targetConceptKey?: string | null;
  priority: string;
}

export interface TutorAnswerSafetyIssueDto {
  code: string;
  severity: string;
  userSafeMessage: string;
}

export interface TutorResponseQualityIssueDto {
  code: string;
  severity: string;
  userSafeMessage: string;
}

export interface TutorResponsePolicyDto {
  teachingMove: string;
  responseDepth: string;
  groundingPolicy: string;
  remediationPolicy: string;
  toolPolicy: string;
  answerSafety: string;
  qualityStatus: string;
  activeConceptKey?: string | null;
  activePlanStepId?: string | null;
  sourceReadiness: string;
  latestAssessmentMode: string;
  latestMisconceptionConfidence: string;
  contextUse: TutorContextUseDto[];
  nextActions: TutorNextLearningActionDto[];
  safetyIssues: TutorAnswerSafetyIssueDto[];
  warnings: TutorResponseQualityIssueDto[];
  generatedAt: string;
}

export interface TutorResponsePolicyRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  tutorTurnStateId?: string | null;
  tutorActionTraceId?: string | null;
  userMessage?: string | null;
  activeQuizUnsubmitted?: boolean;
}

export interface TutorResponseQualityEvaluationRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  tutorTurnStateId?: string | null;
  tutorActionTraceId?: string | null;
  assistantAnswer: string;
  activeQuizUnsubmitted?: boolean;
  policy?: TutorResponsePolicyDto | null;
}

export interface TutorResponseQualityEvaluationDto {
  qualityStatus: string;
  contextUseScore: number;
  groundingScore: number;
  pedagogyScore: number;
  remediationScore: number;
  safetyScore: number;
  toolUseScore: number;
  blockingIssues: TutorResponseQualityIssueDto[];
  warningIssues: TutorResponseQualityIssueDto[];
  policy: TutorResponsePolicyDto;
  evaluatedAt: string;
}

export interface AssessmentCalibrationRun {
  id: string;
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
  topicId?: string | null;
  sessionId?: string | null;
  quizRunId?: string | null;
  assessmentMode?: string;
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
  latestLearningImpact?: QuizResultLearningImpactDto | null;
  decision?: {
    id: string;
    assessmentItemId: string;
    conceptKey: string;
    assessmentMode: string;
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
  isCorrect?: boolean;
  explanation?: string;
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
  assessmentMode?: string;
  sourceReadiness?: string;
  wikiReviewHint?: string;
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

export interface ToolRuntimeRequest {
  toolId: string;
  caller?: "tutor" | "korteks" | "plan" | "wiki" | "quiz" | "frontend" | "internal" | string;
  topicId?: string | null;
  sessionId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  tutorTurnStateId?: string | null;
  tutorActionTraceId?: string | null;
  purpose?: string;
  riskLevel?: "low" | "medium" | "high" | string;
  inputSummary?: string | null;
}

export interface ToolRuntimeDecision {
  traceId?: string | null;
  toolId: string;
  allowed: boolean;
  decision: string;
  reasonCode: string;
  userSafeReason: string;
  requiredEvidenceMode: string;
  maxResultCount?: number | null;
  timeoutMs?: number | null;
  canGroundClaims: boolean;
  shouldWriteTutorMemory: boolean;
  shouldWriteEvidence: boolean;
  shouldRecordTelemetry: boolean;
}

export interface ToolRuntimeEvidence {
  evidenceType: string;
  label: string;
  url?: string | null;
  provider?: string | null;
  confidence?: number | null;
}

export interface ToolRuntimeTrace {
  id: string;
  toolId: string;
  caller: string;
  topicId?: string | null;
  sessionId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  tutorTurnStateId?: string | null;
  tutorActionTraceId?: string | null;
  purpose: string;
  decision: string;
  status: string;
  riskLevel: string;
  canGroundClaims: boolean;
  inputSummary?: string | null;
  safeResultSummary?: string | null;
  evidenceItems: ToolRuntimeEvidence[];
  fallbackReason?: string | null;
  errorCode?: string | null;
  latencyMs: number;
  createdAt: string;
  completedAt?: string | null;
}

export interface ToolRuntimeTracesResponse {
  traces: ToolRuntimeTrace[];
  count: number;
  contract: "tool_runtime_trace_v1" | string;
}

export interface ToolGovernanceSummary {
  traceCount: number;
  allowedCount: number;
  deniedCount: number;
  degradedCount: number;
  evidenceProducingCount: number;
  legacyToolPlanes: string[];
  recentTraces: ToolRuntimeTrace[];
}

export interface LearningRuntimeTraceDto {
  id: string;
  correlationId?: string | null;
  topicId?: string | null;
  sessionId?: string | null;
  category: string;
  operation: string;
  status: string;
  severity: string;
  safeMessage: string;
  latencyMs?: number | null;
  provider?: string | null;
  model?: string | null;
  promptTokens?: number | null;
  completionTokens?: number | null;
  totalTokens?: number | null;
  estimatedCostUsd?: number | null;
  costStatus: string;
  fallbackReason?: string | null;
  errorCode?: string | null;
  isDegraded: boolean;
  isDenied: boolean;
  fallbackUsed: boolean;
  evidenceCount: number;
  toolCount: number;
  artifactCount: number;
  sourceCount: number;
  traceLinks: Record<string, string>;
  safeMetadata: Record<string, string>;
  createdAt: string;
  completedAt?: string | null;
}

export interface LearningRuntimeTracesResponseDto {
  traces: LearningRuntimeTraceDto[];
  count: number;
  contract: "learning_runtime_trace_v1" | string;
}

export interface LearningRuntimeCostDto {
  status: string;
  totalTokens?: number | null;
  estimatedCostUsd?: number | null;
  userSafeMessage: string;
}

export interface LearningRuntimeServiceHealthDto {
  service: string;
  status: string;
  traceCount: number;
  degradedCount: number;
  failedCount: number;
  averageLatencyMs?: number | null;
  userSafeMessage: string;
}

export interface LearningRuntimeHealthDto {
  status: string;
  traceCount: number;
  correlatedTraceCount: number;
  missingCorrelationCount: number;
  degradedCount: number;
  deniedCount: number;
  failedCount: number;
  fallbackCount: number;
  costSummary: LearningRuntimeCostDto;
  services: LearningRuntimeServiceHealthDto[];
  userSafeWarnings: string[];
  generatedAt: string;
}

export interface LearningRuntimeCorrelationDto {
  correlationId: string;
  status: string;
  participatedServices: string[];
  degradedServices: string[];
  fallbackReasons: string[];
  costSummary: LearningRuntimeCostDto;
  traces: LearningRuntimeTraceDto[];
  userSafeWarnings: string[];
}

export interface LearningRuntimeFlowSummaryDto {
  topicId?: string | null;
  sessionId?: string | null;
  correlationId: string;
  latestTraceAt?: string | null;
  status: string;
  participatedServices: string[];
  degradedServices: string[];
  fallbackReasons: string[];
  costSummary: LearningRuntimeCostDto;
  planQuizTutorSyncStatus: string;
  evidenceCount: number;
  toolCount: number;
  artifactCount: number;
  sourceCount: number;
  userSafeWarnings: string[];
}

export interface LearningRuntimePrivacyCheckRequestDto {
  metadataJson?: string | null;
  metadata?: Record<string, string> | null;
}

export interface LearningRuntimePrivacyCheckDto {
  status: string;
  isSafe: boolean;
  blockedTerms: string[];
  safeMetadata: Record<string, string>;
  userSafeMessage: string;
}

export interface AgenticTrustIssueDto {
  category: string;
  severity: string;
  affectedSurface: string;
  userSafeLabel: string;
  userSafeRemediation: string;
  detectedAt: string;
}

export interface AgenticTrustCheckRequestDto {
  topicId?: string | null;
  sessionId?: string | null;
  correlationId?: string | null;
  surface?: string;
  content?: string | null;
  toolId?: string | null;
  caller?: string | null;
  purpose?: string | null;
  riskLevel?: string | null;
  activeQuizUnsubmitted?: boolean;
  citations?: ValidateSourceCitationDto[];
  metadata?: Record<string, string> | null;
  metadataJson?: string | null;
}

export interface AgenticTrustCheckResultDto {
  surface: string;
  decision: string;
  status: string;
  allowed: boolean;
  issues: AgenticTrustIssueDto[];
  userSafeWarnings: string[];
  runtimeTraceId?: string | null;
  checkedAt: string;
}

export interface AgenticTrustRuntimeSummaryDto {
  status: string;
  checkCount: number;
  blockedCount: number;
  degradedCount: number;
  issuesByCategory: Record<string, number>;
  recentIssues: AgenticTrustIssueDto[];
  generatedAt: string;
}

export interface KorteksSourceEvidence {
  provider: string;
  toolName?: string;
  url: string;
  title: string;
  snippet?: string | null;
  publishedAt?: string | null;
  retrievedAt?: string;
  relevanceScore?: number | null;
  sourceType?: string | null;
  externalId?: string | null;
  warning?: string | null;
}

export interface KorteksSynthesisItem {
  kind: string;
  text: string;
  confidence: string;
  evidenceBasis: string;
}

export interface KorteksEvidenceSummary {
  groundingStatus: string;
  sourceConfidence: string;
  sourceCount: number;
  successfulToolCallCount: number;
  failedToolCallCount: number;
  hasUrlBackedEvidence: boolean;
  isFallback: boolean;
}

export interface KorteksSynthesisIssue {
  code: string;
  severity: string;
  userSafeMessage: string;
}

export interface KorteksResearchSynthesis {
  topic: string;
  sourceConfidence: string;
  keyFacts: KorteksSynthesisItem[];
  learningRoute: KorteksSynthesisItem[];
  prerequisites: KorteksSynthesisItem[];
  misconceptions: KorteksSynthesisItem[];
  practiceOrder: KorteksSynthesisItem[];
  quizScope: KorteksSynthesisItem[];
  tutorTeachingHints: KorteksSynthesisItem[];
  wikiNotebookSeeds: KorteksSynthesisItem[];
  sources: KorteksSourceEvidence[];
  providerWarnings: string[];
  generatedAt: string;
}

export interface KorteksConsumerContext {
  consumer: string;
  usagePolicy: string;
  promptBlock: string;
  mustUse: string[];
  mayUse: string[];
  mustNotUse: string[];
}

export interface KorteksConsumerContexts {
  plan: KorteksConsumerContext;
  quiz: KorteksConsumerContext;
  tutor: KorteksConsumerContext;
  wiki: KorteksConsumerContext;
}

export interface KorteksResearchWorkflow {
  id: string;
  topicId?: string | null;
  sessionId?: string | null;
  planRequestId?: string | null;
  activeLessonSnapshotId?: string | null;
  studentContextSnapshotId?: string | null;
  topic: string;
  status: string;
  workflowVersion: string;
  groundingMode: string;
  sourceConfidence: string;
  sourceCount: number;
  toolCallCount: number;
  canGroundTutorClaims: boolean;
  evidenceSummary: KorteksEvidenceSummary;
  synthesis: KorteksResearchSynthesis;
  consumerContexts: KorteksConsumerContexts;
  safetyIssues: KorteksSynthesisIssue[];
  promptBlock: string;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
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
  runtimeTelemetry: LearningRuntimeHealthDto;
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

export interface ExamSourceVerificationDto {
  verificationStatus: "unverified" | "source_backed" | "official_verified" | string;
  canClaimOfficial: boolean;
  userSafeVerificationLabel: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  verifiedAt?: string | null;
}

export interface ExamOutcomeDto {
  id: string;
  code: string;
  name: string;
  description: string;
  sortOrder: number;
}

export interface ExamTopicDto {
  id: string;
  parentExamTopicId?: string | null;
  code: string;
  name: string;
  description: string;
  sortOrder: number;
  outcomes: ExamOutcomeDto[];
  children: ExamTopicDto[];
}

export interface ExamSubjectDto {
  id: string;
  code: string;
  name: string;
  description: string;
  sortOrder: number;
  topics: ExamTopicDto[];
}

export interface ExamSectionDto {
  id: string;
  code: string;
  name: string;
  description: string;
  sortOrder: number;
  subjects: ExamSubjectDto[];
}

export interface ExamVariantDto {
  id: string;
  code: string;
  name: string;
  description: string;
  sortOrder: number;
  sections: ExamSectionDto[];
}

export interface ExamContentPackDto {
  id: string;
  examDefinitionId: string;
  code: string;
  name: string;
  description: string;
  visibility: "system" | "user" | string;
  sourceOrigin: string;
  licenseStatus: string;
  verificationStatus: "unverified" | "source_backed" | "official_verified" | string;
  canClaimOfficial: boolean;
  userSafeVerificationLabel: string;
  sourceVerification: ExamSourceVerificationDto;
  status: string;
  publishedAt?: string | null;
}

export interface ExamDefinitionDto {
  id: string;
  code: string;
  name: string;
  description: string;
  examFamily: string;
  visibility: "system" | "user" | string;
  verificationStatus: "unverified" | "source_backed" | "official_verified" | string;
  canClaimOfficial: boolean;
  userSafeVerificationLabel: string;
  sourceVerification: ExamSourceVerificationDto;
  variants: ExamVariantDto[];
  contentPacks: ExamContentPackDto[];
}

export interface ExamOutcomeImportDto {
  code: string;
  name: string;
  description?: string;
  sortOrder?: number;
}

export interface ExamTopicImportDto {
  code: string;
  name: string;
  description?: string;
  sortOrder?: number;
  outcomes?: ExamOutcomeImportDto[];
  children?: ExamTopicImportDto[];
}

export interface ExamSubjectImportDto {
  code: string;
  name: string;
  description?: string;
  sortOrder?: number;
  topics: ExamTopicImportDto[];
}

export interface ExamSectionImportDto {
  code: string;
  name: string;
  description?: string;
  sortOrder?: number;
  subjects: ExamSubjectImportDto[];
}

export interface ExamVariantImportDto {
  code: string;
  name: string;
  description?: string;
  sortOrder?: number;
  sections: ExamSectionImportDto[];
}

export interface ExamTreeImportDto {
  examCode: string;
  examName: string;
  description?: string;
  examFamily?: string;
  verificationStatus?: "unverified" | "source_backed" | "official_verified" | string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  contentPackCode?: string;
  contentPackName?: string;
  sourceOrigin?: string;
  licenseStatus?: string;
  variants: ExamVariantImportDto[];
}

export interface QuestionValidationResultDto {
  isValid: boolean;
  errors: string[];
  warnings: string[];
  accessibility: QuestionAccessibilityValidationDto[];
}

export interface QuestionAccessibilityValidationDto {
  targetType: string;
  targetId?: string | null;
  code: string;
  severity: string;
}

export interface QuestionAssetDto {
  id: string;
  ownershipState: "system" | "user" | string;
  assetType: string;
  storageKey: string;
  fileName: string;
  mimeType: string;
  sizeBytes: number;
  sha256Hash: string;
  sourceRegistryItemId?: string | null;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  licenseStatus: string;
  verificationStatus: string;
  altText?: string | null;
  caption?: string | null;
  longDescription?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateQuestionAssetDto {
  assetType?: string;
  storageKey: string;
  fileName: string;
  mimeType?: string;
  sizeBytes?: number;
  sha256Hash: string;
  sourceRegistryItemId?: string | null;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  licenseStatus?: string;
  verificationStatus?: string;
  altText?: string | null;
  caption?: string | null;
  longDescription?: string | null;
}

export interface QuestionContentBlockDto {
  id?: string | null;
  blockType: string;
  text?: string | null;
  contentJson?: string | null;
  assetId?: string | null;
  asset?: QuestionAssetDto | null;
  sortOrder: number;
  altText?: string | null;
  caption?: string | null;
  longDescription?: string | null;
}

export interface CreateQuestionContentBlockDto {
  blockType?: string;
  text?: string | null;
  contentJson?: string | null;
  assetId?: string | null;
  sortOrder?: number;
  altText?: string | null;
  caption?: string | null;
  longDescription?: string | null;
}

export interface QuestionOptionContentBlockDto {
  id?: string | null;
  blockType: string;
  text?: string | null;
  contentJson?: string | null;
  assetId?: string | null;
  asset?: QuestionAssetDto | null;
  sortOrder: number;
  altText?: string | null;
  caption?: string | null;
}

export interface CreateQuestionOptionContentBlockDto {
  blockType?: string;
  text?: string | null;
  contentJson?: string | null;
  assetId?: string | null;
  sortOrder?: number;
  altText?: string | null;
  caption?: string | null;
}

export interface QuestionStimulusDto {
  id: string;
  ownershipState: "system" | "user" | string;
  title: string;
  stimulusType: string;
  contentText?: string | null;
  contentJson?: string | null;
  sourceRegistryItemId?: string | null;
  curriculumNodeId?: string | null;
  verificationStatus: string;
  licenseStatus: string;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateQuestionStimulusDto {
  title: string;
  stimulusType?: string;
  contentText?: string | null;
  contentJson?: string | null;
  sourceRegistryItemId?: string | null;
  curriculumNodeId?: string | null;
  verificationStatus?: string;
  licenseStatus?: string;
}

export interface QuestionStimulusLinkDto {
  questionStimulusId: string;
  sortOrder?: number;
}

export interface QuestionOptionDto {
  id?: string | null;
  optionKey: string;
  text: string;
  isCorrect: boolean;
  sortOrder: number;
  contentBlocks?: QuestionOptionContentBlockDto[];
}

export interface QuestionExplanationDto {
  id?: string | null;
  explanationText: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  visibility: string;
  isSafeForLearners: boolean;
}

export interface QuestionTagDto {
  id?: string | null;
  tag: string;
}

export interface QuestionOutcomeLinkDto {
  id?: string | null;
  examOutcomeId: string;
  isPrimary: boolean;
  linkStrength: number;
}

export interface QuestionItemDto {
  id: string;
  ownershipState: "system" | "user" | string;
  examDefinitionId: string;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  questionType: "multiple_choice" | "paragraph" | "math_problem" | "grammar" | "vocabulary" | "reading_comprehension" | string;
  stem: string;
  difficulty: "easy" | "medium" | "hard" | string;
  cognitiveSkill: string;
  qualityStatus: "draft" | "needs_review" | "approved" | "published" | "rejected" | string;
  licenseStatus: "unknown" | "user_provided" | "licensed" | "open" | "restricted" | string;
  sourceOrigin: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  explanation: string;
  createdAt: string;
  updatedAt: string;
  options: QuestionOptionDto[];
  explanations: QuestionExplanationDto[];
  tags: QuestionTagDto[];
  outcomeLinks: QuestionOutcomeLinkDto[];
  contentBlocks: QuestionContentBlockDto[];
  stimuli: QuestionStimulusDto[];
  validation: QuestionValidationResultDto;
}

export interface CreateQuestionDto {
  examDefinitionId: string;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  questionType?: string;
  stem: string;
  difficulty?: string;
  cognitiveSkill?: string;
  licenseStatus?: string;
  sourceOrigin?: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  explanation?: string;
  options: QuestionOptionDto[];
  explanations?: QuestionExplanationDto[];
  tags?: QuestionTagDto[];
  outcomeLinks?: QuestionOutcomeLinkDto[];
  contentBlocks?: CreateQuestionContentBlockDto[];
  stimuli?: QuestionStimulusLinkDto[];
}

export interface UpdateQuestionDto {
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  questionType?: string;
  stem?: string;
  difficulty?: string;
  cognitiveSkill?: string;
  qualityStatus?: string;
  licenseStatus?: string;
  sourceOrigin?: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  explanation?: string;
  options?: QuestionOptionDto[];
  explanations?: QuestionExplanationDto[];
  tags?: QuestionTagDto[];
  outcomeLinks?: QuestionOutcomeLinkDto[];
  contentBlocks?: CreateQuestionContentBlockDto[];
  stimuli?: QuestionStimulusLinkDto[];
}

export interface QuestionBankFilterDto {
  examDefinitionId?: string;
  examVariantId?: string;
  examSectionId?: string;
  examSubjectId?: string;
  examTopicId?: string;
  examOutcomeId?: string;
  qualityStatus?: string;
  questionType?: string;
  difficulty?: string;
  take?: number;
}

export interface QuestionImportOptionDto {
  optionKey: string;
  text: string;
  isCorrect: boolean;
  sortOrder?: number;
}

export interface QuestionImportSourceDto {
  sourceOrigin?: string;
  licenseStatus?: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
}

export interface QuestionImportItemDto {
  externalId?: string | null;
  examDefinitionId?: string | null;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  examCode?: string | null;
  variantCode?: string | null;
  sectionCode?: string | null;
  subjectCode?: string | null;
  topicCode?: string | null;
  outcomeCode?: string | null;
  questionType?: string;
  stem: string;
  options: QuestionImportOptionDto[];
  explanation?: string;
  difficulty?: string;
  cognitiveSkill?: string;
  tags?: string[];
  sourceOrigin?: string;
  licenseStatus?: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  source?: QuestionImportSourceDto | null;
}

export interface QuestionImportRequestDto {
  items: QuestionImportItemDto[];
}

export interface QuestionImportValidationIssueDto {
  code: string;
  severity: "error" | "warning" | string;
  message: string;
}

export interface QuestionImportPreviewItemDto {
  id: string;
  rowIndex: number;
  externalId?: string | null;
  status: "accepted" | "rejected" | "duplicate" | string;
  issues: QuestionImportValidationIssueDto[];
  isDuplicate: boolean;
  duplicateQuestionId?: string | null;
  createdQuestionId?: string | null;
  normalizedQuestion?: CreateQuestionDto | null;
}

export interface QuestionImportPreviewDto {
  id: string;
  status: "pending" | "approved" | "expired" | string;
  importFormat: string;
  packageTitle?: string | null;
  packageVersion?: string | null;
  totalCount: number;
  acceptedCount: number;
  rejectedCount: number;
  warningCount: number;
  createdAt: string;
  expiresAt: string;
  assets: QuestionImportAssetDto[];
  stimuli: QuestionImportStimulusDto[];
  items: QuestionImportPreviewItemDto[];
}

export interface QuestionImportApprovalDto {
  importPreviewId: string;
}

export interface QuestionImportResultDto {
  importPreviewId: string;
  status: string;
  createdCount: number;
  skippedCount: number;
  rejectedCount: number;
  createdQuestionIds: string[];
  issues: QuestionImportValidationIssueDto[];
}

export interface QuestionImportPackageDto {
  packageVersion?: string;
  packageTitle?: string;
  sourceOrigin?: string;
  licenseStatus?: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  examDefinitionId?: string | null;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  examCode?: string | null;
  variantCode?: string | null;
  sectionCode?: string | null;
  subjectCode?: string | null;
  topicCode?: string | null;
  outcomeCode?: string | null;
  assets: QuestionImportAssetDto[];
  stimuli: QuestionImportStimulusDto[];
  questions: QuestionImportRichQuestionDto[];
}

export interface QuestionImportAssetDto {
  externalAssetId: string;
  assetType?: string;
  storageKey?: string;
  relativePath?: string | null;
  fileName?: string;
  mimeType?: string;
  sizeBytes?: number;
  sha256Hash: string;
  sourceRegistryItemId?: string | null;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  licenseStatus?: string;
  verificationStatus?: string;
  altText?: string | null;
  caption?: string | null;
  longDescription?: string | null;
}

export interface QuestionImportStimulusDto {
  externalStimulusId: string;
  title: string;
  stimulusType?: string;
  contentText?: string | null;
  contentJson?: string | null;
  sourceRegistryItemId?: string | null;
  curriculumNodeId?: string | null;
  licenseStatus?: string;
  verificationStatus?: string;
}

export interface QuestionImportRichQuestionDto {
  externalId?: string | null;
  examDefinitionId?: string | null;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  examCode?: string | null;
  variantCode?: string | null;
  sectionCode?: string | null;
  subjectCode?: string | null;
  topicCode?: string | null;
  outcomeCode?: string | null;
  questionType?: string;
  stem?: string;
  difficulty?: string;
  cognitiveSkill?: string;
  sourceOrigin?: string | null;
  licenseStatus?: string | null;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  explanation?: string;
  contentBlocks: QuestionImportContentBlockDto[];
  options: QuestionImportRichOptionDto[];
  explanations?: QuestionExplanationDto[];
  tags?: string[];
  outcomeLinks?: QuestionOutcomeLinkDto[];
  externalStimulusIds?: string[];
}

export interface QuestionImportContentBlockDto {
  blockType?: string;
  text?: string | null;
  contentJson?: string | null;
  externalAssetId?: string | null;
  sortOrder?: number;
  altText?: string | null;
  caption?: string | null;
  longDescription?: string | null;
}

export interface QuestionImportRichOptionDto {
  optionKey: string;
  text?: string;
  isCorrect: boolean;
  sortOrder?: number;
  contentBlocks: QuestionImportContentBlockDto[];
}

export interface QuestionImportTextAdapterRequestDto {
  content: string;
  sourceOrigin?: string;
  licenseStatus?: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  examDefinitionId?: string | null;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  examCode?: string | null;
  variantCode?: string | null;
  sectionCode?: string | null;
  subjectCode?: string | null;
  topicCode?: string | null;
  outcomeCode?: string | null;
}

export interface QuestionDraftGenerationContextDto {
  examDefinitionId?: string | null;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  examCode?: string | null;
  variantCode?: string | null;
  sectionCode?: string | null;
  subjectCode?: string | null;
  topicCode?: string | null;
  outcomeCode?: string | null;
}

export interface QuestionDraftGenerationSourceDto {
  sourceTitle: string;
  sourceUrl?: string | null;
  sourceOrigin?: string;
  licenseStatus?: string;
  sourceText?: string | null;
  structuredSourceContext?: string[];
}

export interface QuestionDraftGenerationRequestDto {
  context: QuestionDraftGenerationContextDto;
  source: QuestionDraftGenerationSourceDto;
  questionType?: string;
  desiredCount?: number;
  difficulty?: string;
  cognitiveSkill?: string;
}

export interface QuestionDraftOptionDto {
  optionKey: string;
  text: string;
  isCorrect: boolean;
  sortOrder: number;
}

export interface QuestionDraftCandidateDto {
  externalId?: string | null;
  questionType: string;
  stem: string;
  options: QuestionDraftOptionDto[];
  explanation: string;
  difficulty: string;
  cognitiveSkill: string;
  tags: string[];
  sourceOrigin: string;
  licenseStatus: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
}

export interface QuestionDraftGenerationIssueDto {
  code: string;
  severity: "error" | "warning" | string;
  message: string;
}

export interface QuestionDraftPreviewItemDto {
  id: string;
  rowIndex: number;
  externalId?: string | null;
  status: "accepted" | "rejected" | "duplicate" | string;
  isDuplicate: boolean;
  duplicateQuestionId?: string | null;
  createdQuestionId?: string | null;
  candidate?: QuestionDraftCandidateDto | null;
  issues: QuestionDraftGenerationIssueDto[];
}

export interface QuestionDraftPreviewDto {
  id: string;
  importPreviewId: string;
  status: "pending" | "approved" | "expired" | "rejected" | string;
  totalRequested: number;
  generatedCount: number;
  acceptedDraftCount: number;
  rejectedCount: number;
  warningCount: number;
  createdAt: string;
  expiresAt: string;
  items: QuestionDraftPreviewItemDto[];
  issues: QuestionDraftGenerationIssueDto[];
}

export interface QuestionDraftApprovalDto {
  draftPreviewId: string;
}

export interface QuestionDraftApprovalResultDto {
  draftPreviewId: string;
  importPreviewId: string;
  status: string;
  createdCount: number;
  skippedCount: number;
  rejectedCount: number;
  createdQuestionIds: string[];
  issues: QuestionDraftGenerationIssueDto[];
}

export interface SourceRegistryItemDto {
  id: string;
  ownershipState: string;
  sourceKey: string;
  title: string;
  sourceUrl?: string | null;
  sourceType: string;
  publisher: string;
  licenseStatus: string;
  verificationStatus: string;
  canClaimOfficial: boolean;
  userSafeVerificationLabel: string;
  sourceContentHash?: string | null;
  verifiedAt?: string | null;
  visibility: string;
  createdAt: string;
  updatedAt: string;
  licenseReviews: ContentLicenseReviewDto[];
}

export interface RegisterSourceRegistryItemDto {
  sourceKey: string;
  title: string;
  sourceUrl?: string | null;
  sourceType?: string;
  publisher?: string;
  licenseStatus?: string;
  verificationStatus?: string;
  sourceContentHash?: string | null;
}

export interface VerifySourceRegistryItemDto {
  verificationStatus?: string;
  verificationMethod?: string;
  evidenceLocator?: string | null;
  internalNotes?: string | null;
}

export interface ReviewSourceLicenseDto {
  licenseStatus?: string;
  reviewStatus?: string;
  decisionReason?: string;
}

export interface ContentLicenseReviewDto {
  id: string;
  licenseStatus: string;
  reviewStatus: string;
  publishAllowed: boolean;
  decisionReason: string;
  createdAt: string;
}

export interface CurriculumVersionDto {
  id: string;
  examDefinitionId: string;
  sourceRegistryItemId?: string | null;
  ownershipState: string;
  code: string;
  name: string;
  description: string;
  versionLabel: string;
  status: string;
  verificationStatus: string;
  canClaimOfficial: boolean;
    userSafeVerificationLabel: string;
    sourceSnapshotHash?: string | null;
    supersededByCurriculumVersionId?: string | null;
    deprecatedAt?: string | null;
    deprecatedReason?: string | null;
    archivedAt?: string | null;
    effectiveFrom?: string | null;
    effectiveUntil?: string | null;
    nodes: CurriculumNodeDto[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateCurriculumVersionDto {
  examDefinitionId: string;
  sourceRegistryItemId?: string | null;
  code: string;
  name: string;
  description?: string;
  versionLabel?: string;
  status?: string;
  verificationStatus?: string;
  effectiveFrom?: string | null;
    effectiveUntil?: string | null;
  }

  export interface DeprecateCurriculumVersionDto {
    deprecatedReason?: string;
  }

  export interface SupersedeCurriculumVersionDto {
    replacementCurriculumVersionId: string;
    deprecatedReason?: string;
  }

  export interface CurriculumNodeDto {
  id: string;
  curriculumVersionId: string;
  parentCurriculumNodeId?: string | null;
  nodeType: string;
  code: string;
  title: string;
  description: string;
    verificationStatus: string;
    canClaimOfficial: boolean;
    sourceAnchor?: string | null;
    sourceLocator?: string | null;
    sortOrder: number;
    children: CurriculumNodeDto[];
  }

export interface CreateCurriculumNodeDto {
  parentCurriculumNodeId?: string | null;
  nodeType?: string;
  code: string;
  title: string;
    description?: string;
    verificationStatus?: string;
    sourceAnchor?: string | null;
    sourceLocator?: string | null;
    sortOrder?: number;
  }

export interface CurriculumOutcomeMappingDto {
  id: string;
  curriculumVersionId: string;
  curriculumNodeId: string;
  examOutcomeId: string;
    sourceRegistryItemId?: string | null;
    mappingType: string;
    confidenceStatus: string;
    reviewStatus: string;
    verificationStatus: string;
    canClaimOfficial: boolean;
    sourceLocator?: string | null;
    pageNumber?: number | null;
    sectionTitle?: string | null;
    clause?: string | null;
    anchorText?: string | null;
    evidenceUrl?: string | null;
    userSafeVerificationLabel: string;
    createdAt: string;
  }

export interface CreateCurriculumOutcomeMappingDto {
  curriculumNodeId: string;
  examOutcomeId: string;
    sourceRegistryItemId?: string | null;
    mappingType?: string;
    confidenceStatus?: string;
    reviewStatus?: string;
    verificationStatus?: string;
    sourceLocator?: string | null;
    pageNumber?: number | null;
    sectionTitle?: string | null;
    clause?: string | null;
    anchorText?: string | null;
    evidenceUrl?: string | null;
  }

export interface CurriculumOutcomeSourceDto {
  examOutcomeId: string;
  mappings: CurriculumOutcomeMappingDto[];
}

export interface CentralExamVariantDto {
  variantCode: string;
  displayName: string;
  availabilityStatus: string;
}

export interface CentralExamCapabilityDto {
  hasQuestionBank: boolean;
  hasPractice: boolean;
  hasMiniDeneme: boolean;
  hasCountdown: boolean;
  hasStudyPlan: boolean;
}

export interface CentralExamDto {
  examCode: string;
  displayName: string;
  description: string;
  availabilityStatus: string;
  verificationStatus: string;
  canClaimOfficial: boolean;
  userSafeVerificationLabel: string;
  supportedVariants: CentralExamVariantDto[];
  capabilities: CentralExamCapabilityDto;
}

export interface CentralExamCountdownDto {
  examCode: string;
  examDate?: string | null;
  daysRemaining?: number | null;
  verificationStatus: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  userSafeLabel: string;
}

export interface CentralExamTopicDto {
  id: string;
  code: string;
  name: string;
  practiceReadyCount: number;
  children: CentralExamTopicDto[];
}

export interface CentralExamSubjectDto {
  id: string;
  code: string;
  name: string;
  topics: CentralExamTopicDto[];
}

export interface CentralExamSectionDto {
  id: string;
  code: string;
  name: string;
  subjects: CentralExamSubjectDto[];
}

export interface CentralExamQuestionCountDto {
  practiceReadyCount: number;
  systemPublishedCount: number;
  userPublishedCount: number;
  callerDraftCount: number;
  callerNeedsReviewCount: number;
}

export interface ExamLearningContextDto {
  examDefinitionId?: string | null;
  examCode?: string | null;
  examVariantId?: string | null;
  variantCode?: string | null;
  examSectionId?: string | null;
  sectionCode?: string | null;
  examSubjectId?: string | null;
  subjectCode?: string | null;
  examTopicId?: string | null;
  topicCode?: string | null;
  examOutcomeId?: string | null;
  outcomeCode?: string | null;
}

export interface CentralExamPracticeEntryDto {
  examCode: string;
  slug: string;
  title: string;
  description: string;
  hasPracticeReadyQuestions: boolean;
  practiceReadyCount: number;
  emptyState: string;
  recommendedAction: string;
  examContext: ExamLearningContextDto;
}

export interface CentralExamStudyHomeDto {
  examCode: string;
  displayName: string;
  description: string;
  verificationStatus: string;
  canClaimOfficial: boolean;
  userSafeVerificationLabel: string;
  countdown: CentralExamCountdownDto;
  supportedVariants: CentralExamVariantDto[];
  sections: CentralExamSectionDto[];
  practiceReadyCounts: CentralExamQuestionCountDto;
  recommendedEntryPoint?: CentralExamPracticeEntryDto | null;
  capabilities: CentralExamCapabilityDto;
  emptyState: string;
  generatedAt: string;
}

export interface PracticeStartRequestDto {
  variantCode?: string | null;
  limit?: number;
}

export interface PracticeOptionDto {
  optionKey: string;
  text: string;
  sortOrder: number;
  contentBlocks: PracticeContentBlockDto[];
}

export interface PracticeContentBlockDto {
  blockType: string;
  text?: string | null;
  contentJson?: string | null;
  assetType?: string | null;
  fileName?: string | null;
  mimeType?: string | null;
  sortOrder: number;
  altText?: string | null;
  caption?: string | null;
  longDescription?: string | null;
}

export interface PracticeStimulusDto {
  title: string;
  stimulusType: string;
  contentText?: string | null;
  contentJson?: string | null;
  sortOrder: number;
}

export interface PracticeQuestionDto {
  questionId: string;
  stem: string;
  difficulty: string;
  cognitiveSkill: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  examContext: ExamLearningContextDto;
  stimuli: PracticeStimulusDto[];
  contentBlocks: PracticeContentBlockDto[];
  options: PracticeOptionDto[];
}

export interface PracticeSessionDto {
  practiceSetId: string;
  practiceAttemptId?: string | null;
  status: string;
  emptyState: string;
  totalQuestions: number;
  examContext: ExamLearningContextDto;
  questions: PracticeQuestionDto[];
}

export interface PracticeAnswerDto {
  questionId: string;
  selectedOptionKey?: string | null;
}

export interface PracticeSubmitRequestDto {
  variantCode?: string | null;
  practiceSetId?: string | null;
  answers: PracticeAnswerDto[];
}

export interface PracticeQuestionResultDto {
  questionId: string;
  stem: string;
  selectedOptionKey?: string | null;
  correctOptionKey?: string | null;
  isCorrect: boolean;
  isBlank: boolean;
  explanation: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  examContext: ExamLearningContextDto;
  stimuli: PracticeStimulusDto[];
  contentBlocks: PracticeContentBlockDto[];
  options: PracticeOptionDto[];
}

export interface PracticeTopicBreakdownDto {
  examTopicId?: string | null;
  topicCode?: string | null;
  label: string;
  totalQuestions: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
}

export interface CentralExamPracticeSummaryDto {
  totalQuestions: number;
  answeredCount: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  correctnessRatio: number;
}

export interface CentralExamLearningSignalDto {
  status: string;
  signalCount: number;
  evidenceBasis: string[];
  weakAreas: string[];
}

export interface CentralExamNextActionDto {
  actionType: string;
  title: string;
  reason: string;
  confidenceStatus: string;
  examContext: ExamLearningContextDto;
}

export interface CentralExamStudyContextDto {
  pathLabel: string;
  suggestedWikiPath: string;
  examContext: ExamLearningContextDto;
  focusLabels: string[];
}

export interface CentralExamPracticeAttemptDto {
  id: string;
  status: string;
  examContext: ExamLearningContextDto;
  summary: CentralExamPracticeSummaryDto;
  startedAt: string;
  submittedAt?: string | null;
}

export interface CentralExamPracticeAnswerDto {
  questionId: string;
  selectedOptionKey?: string | null;
  correctOptionKey?: string | null;
  isCorrect: boolean;
  isBlank: boolean;
  examContext: ExamLearningContextDto;
}

export interface PracticeResultDto {
  practiceAttemptId?: string | null;
  status: string;
  totalQuestions: number;
  answeredCount: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  examContext: ExamLearningContextDto;
  results: PracticeQuestionResultDto[];
  topicBreakdown: PracticeTopicBreakdownDto[];
  nextAction?: CentralExamNextActionDto | null;
  learningSignal?: CentralExamLearningSignalDto | null;
  studyContext?: CentralExamStudyContextDto | null;
  tutorRemediationContext: string;
}

export interface QuestionOptionAnalyticsDto {
  optionKey: string;
  selectionCount: number;
  correctSelectionCount: number;
  wrongSelectionCount: number;
  selectionRate: number;
  isCorrectOption: boolean;
  distractorSignal: string;
}

export interface QuestionQualityReviewSignalDto {
  id: string;
  questionItemId: string;
  signalType: string;
  severity: string;
  message: string;
  createdAt: string;
  resolvedAt?: string | null;
}

export interface QuestionItemAnalyticsDto {
  questionItemId: string;
  examDefinitionId: string;
  examVariantId?: string | null;
  examSectionId?: string | null;
  examSubjectId?: string | null;
  examTopicId?: string | null;
  examOutcomeId?: string | null;
  attemptCount: number;
  answeredCount: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  correctnessRate: number;
  blankRate: number;
  difficultyEstimate: string;
  discriminationStatus: string;
  qualitySignal: string;
  sampleSizeStatus: string;
  lastCalculatedAt: string;
  options: QuestionOptionAnalyticsDto[];
  reviewSignals: QuestionQualityReviewSignalDto[];
}

export interface CentralExamQualityTopicCoverageDto {
  examSubjectId?: string | null;
  subjectCode?: string | null;
  examTopicId?: string | null;
  topicCode?: string | null;
  examOutcomeId?: string | null;
  outcomeCode?: string | null;
  publishedQuestionCount: number;
  practiceReadyCount: number;
  callerDraftCount: number;
  callerNeedsReviewCount: number;
  coverageStatus: string;
  averageDifficultyEstimate?: string | null;
}

export interface CentralExamQualityOverviewDto {
  examCode: string;
  variantCode?: string | null;
  userSafeLabel: string;
  visibleQuestionCount: number;
  publishedQuestionCount: number;
  analyticsSnapshotCount: number;
  needsReviewSignalCount: number;
  lowCoverageTopicCount: number;
  generatedAt: string;
  topics: CentralExamQualityTopicCoverageDto[];
}

export interface CentralExamBlueprintCoverageDto {
  examCode: string;
  variantCode?: string | null;
  userSafeLabel: string;
  topicCount: number;
  noContentCount: number;
  lowContentCount: number;
  usableCount: number;
  strongCount: number;
  topics: CentralExamQualityTopicCoverageDto[];
}

export interface RecalculateQuestionAnalyticsResultDto {
  questionItemId: string;
  recalculated: boolean;
  analytics?: QuestionItemAnalyticsDto | null;
}

export interface RecalculateExamAnalyticsResultDto {
  examCode: string;
  variantCode?: string | null;
  recalculatedQuestionCount: number;
  calculatedAt: string;
}

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

export interface CentralExamDenemeBlueprintSectionDto {
  id: string;
  sortOrder: number;
  questionCount: number;
  availableQuestionCount: number;
  label: string;
  examContext: ExamLearningContextDto;
}

export interface CentralExamDenemeBlueprintDto {
  id: string;
  code: string;
  name: string;
  description: string;
  visibility: string;
  verificationStatus: string;
  canClaimOfficial: boolean;
  userSafeVerificationLabel: string;
  durationMinutes?: number | null;
  totalQuestionCount: number;
  availableQuestionCount: number;
  hasEnoughQuestions: boolean;
  emptyState: string;
  examContext: ExamLearningContextDto;
  sections: CentralExamDenemeBlueprintSectionDto[];
}

export interface CentralExamDenemeStartRequestDto {
  variantCode?: string | null;
}

export interface CentralExamDenemeOptionDto {
  optionKey: string;
  text: string;
  sortOrder: number;
}

export interface CentralExamDenemeQuestionDto {
  questionId: string;
  stem: string;
  difficulty: string;
  cognitiveSkill: string;
  sourceTitle?: string | null;
  sourceUrl?: string | null;
  examContext: ExamLearningContextDto;
  options: CentralExamDenemeOptionDto[];
}

export interface CentralExamDenemeSessionDto {
  denemeAttemptId: string;
  blueprintCode: string;
  blueprintName: string;
  status: string;
  emptyState: string;
  durationMinutes?: number | null;
  totalQuestions: number;
  examContext: ExamLearningContextDto;
  questions: CentralExamDenemeQuestionDto[];
}

export interface CentralExamDenemeAnswerDto {
  questionId: string;
  selectedOptionKey?: string | null;
}

export interface CentralExamDenemeSubmitRequestDto {
  denemeAttemptId: string;
  answers: CentralExamDenemeAnswerDto[];
}

export interface CentralExamDenemeSummaryDto {
  totalQuestions: number;
  answeredCount: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  correctnessRatio: number;
}

export interface CentralExamDenemeBreakdownDto {
  examSectionId?: string | null;
  sectionCode?: string | null;
  examSubjectId?: string | null;
  subjectCode?: string | null;
  examTopicId?: string | null;
  topicCode?: string | null;
  label: string;
  totalQuestions: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
}

export interface CentralExamDenemeNextActionDto {
  actionType: string;
  title: string;
  reason: string;
  confidenceStatus: string;
  examContext: ExamLearningContextDto;
}

export interface CentralExamDenemeResultDto {
  denemeAttemptId: string;
  blueprintCode: string;
  blueprintName: string;
  status: string;
  durationMinutes?: number | null;
  summary: CentralExamDenemeSummaryDto;
  examContext: ExamLearningContextDto;
  results: PracticeQuestionResultDto[];
  breakdown: CentralExamDenemeBreakdownDto[];
  nextAction?: CentralExamDenemeNextActionDto | null;
  learningSignal?: CentralExamLearningSignalDto | null;
  studyContext?: CentralExamStudyContextDto | null;
  tutorRemediationContext: string;
}

export interface QuestionReviewEventDto {
  id: string;
  eventType: string;
  fromStage?: string | null;
  toStage?: string | null;
  reason?: string | null;
  safeNote?: string | null;
  createdAt: string;
}

export interface QuestionReviewWorkflowDto {
  id: string;
  questionItemId: string;
  currentStage: string;
  status: string;
  hasAssignedReviewer: boolean;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
  events: QuestionReviewEventDto[];
}

export interface SubmitQuestionReviewDto {
  safeNote?: string | null;
}

export interface AssignQuestionReviewerDto {
  assignedReviewerUserId?: string | null;
  safeNote?: string | null;
}

export interface AdvanceQuestionReviewStageDto {
  toStage: string;
  safeNote?: string | null;
}

export interface RejectQuestionReviewDto {
  reason: string;
}

export interface RetireQuestionDto {
  reason: string;
}

export interface PublishQuestionContentDto {
  safeNote?: string | null;
}

export interface QuestionPublishIssueDto {
  code: string;
  severity: string;
  area: string;
  message: string;
}

export interface QuestionPublishReadinessDto {
  questionItemId: string;
  workflowId?: string | null;
  workflowStage?: string | null;
  workflowStatus?: string | null;
  isReadyToPublish: boolean;
  recommendedNextReviewStage: string;
  blockingIssues: QuestionPublishIssueDto[];
  warningIssues: QuestionPublishIssueDto[];
}

export interface QuestionContentVersionDto {
  id: string;
  questionItemId: string;
  versionNumber: number;
  createdAt: string;
  reason?: string | null;
}
