using Microsoft.EntityFrameworkCore;
using Orka.Core.Entities;
using Orka.Core.Enums;

namespace Orka.Infrastructure.Data;

public class OrkaDbContext : DbContext
{
    public OrkaDbContext(DbContextOptions<OrkaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Topic> Topics { get; set; } = null!;
    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<WikiPage> WikiPages { get; set; } = null!;
    public DbSet<WikiBlock> WikiBlocks { get; set; } = null!;
    public DbSet<Source> Sources { get; set; } = null!;
    public DbSet<QuizAttempt> QuizAttempts { get; set; } = null!;
    public DbSet<QuizRun> QuizRuns { get; set; } = null!;
    public DbSet<LearningSignal> LearningSignals { get; set; } = null!;
    public DbSet<RemediationPlan> RemediationPlans { get; set; } = null!;
    public DbSet<StudyRecommendation> StudyRecommendations { get; set; } = null!;
    public DbSet<ClassroomSession> ClassroomSessions { get; set; } = null!;
    public DbSet<ClassroomInteraction> ClassroomInteractions { get; set; } = null!;
    public DbSet<AgentEvaluation> AgentEvaluations { get; set; } = null!;
    public DbSet<SkillMastery> SkillMasteries { get; set; } = null!;
    public DbSet<LearningSource> LearningSources { get; set; } = null!;
    public DbSet<SourceChunk> SourceChunks { get; set; } = null!;
    public DbSet<AudioOverviewJob> AudioOverviewJobs { get; set; } = null!;
    public DbSet<ReviewItem> ReviewItems { get; set; } = null!;
    public DbSet<Flashcard> Flashcards { get; set; } = null!;
    public DbSet<DailyChallenge> DailyChallenges { get; set; } = null!;
    public DbSet<DailyChallengeSubmission> DailyChallengeSubmissions { get; set; } = null!;
    public DbSet<XpEvent> XpEvents { get; set; } = null!;
    public DbSet<Badge> Badges { get; set; } = null!;
    public DbSet<UserBadge> UserBadges { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<Bookmark> Bookmarks { get; set; } = null!;
    public DbSet<PushSubscription> PushSubscriptions { get; set; } = null!;
    public DbSet<ToolTelemetryEvent> ToolTelemetryEvents { get; set; } = null!;
    public DbSet<CostRecord> CostRecords { get; set; } = null!;
    public DbSet<ConceptGraphSnapshot> ConceptGraphSnapshots { get; set; } = null!;
    public DbSet<LearningConcept> LearningConcepts { get; set; } = null!;
    public DbSet<ConceptRelation> ConceptRelations { get; set; } = null!;
    public DbSet<LearningOutcome> LearningOutcomes { get; set; } = null!;
    public DbSet<OutcomeAlignment> OutcomeAlignments { get; set; } = null!;
    public DbSet<AssessmentItem> AssessmentItems { get; set; } = null!;
    public DbSet<DiagnosticProfile> DiagnosticProfiles { get; set; } = null!;
    public DbSet<ConceptMastery> ConceptMasteries { get; set; } = null!;
    public DbSet<LearningEvent> LearningEvents { get; set; } = null!;
    public DbSet<ConceptGraphQualityRun> ConceptGraphQualityRuns { get; set; } = null!;
    public DbSet<AssessmentQualityRun> AssessmentQualityRuns { get; set; } = null!;
    public DbSet<AssessmentItemStat> AssessmentItemStats { get; set; } = null!;
    public DbSet<KnowledgeTracingState> KnowledgeTracingStates { get; set; } = null!;
    public DbSet<TutorPolicyTrace> TutorPolicyTraces { get; set; } = null!;
    public DbSet<LearningEventSchemaViolation> LearningEventSchemaViolations { get; set; } = null!;
    public DbSet<ResourceConceptAlignment> ResourceConceptAlignments { get; set; } = null!;
    public DbSet<LearningQualityReport> LearningQualityReports { get; set; } = null!;
    public DbSet<TutorWorkingMemorySnapshot> TutorWorkingMemorySnapshots { get; set; } = null!;
    public DbSet<TutorTurnState> TutorTurnStates { get; set; } = null!;
    public DbSet<TutorMemoryPatch> TutorMemoryPatches { get; set; } = null!;
    public DbSet<LearnerProfile> LearnerProfiles { get; set; } = null!;
    public DbSet<LearningStyleSignal> LearningStyleSignals { get; set; } = null!;
    public DbSet<AffectiveSignal> AffectiveSignals { get; set; } = null!;
    public DbSet<CognitiveLoadSignal> CognitiveLoadSignals { get; set; } = null!;
    public DbSet<TutorActionTrace> TutorActionTraces { get; set; } = null!;
    public DbSet<TutorToolCall> TutorToolCalls { get; set; } = null!;
    public DbSet<TeachingArtifact> TeachingArtifacts { get; set; } = null!;
    public DbSet<TutorReflectionUpdate> TutorReflectionUpdates { get; set; } = null!;
    public DbSet<TutorPolicyViolationV2> TutorPolicyViolationsV2 { get; set; } = null!;
    public DbSet<TutorMemoryFragment> TutorMemoryFragments { get; set; } = null!;
    public DbSet<RagEvaluationRun> RagEvaluationRuns { get; set; } = null!;
    public DbSet<RagEvaluationItem> RagEvaluationItems { get; set; } = null!;
    public DbSet<TeachingEvidenceItem> TeachingEvidenceItems { get; set; } = null!;
    public DbSet<TeachingEvidenceProviderHealth> TeachingEvidenceProviderHealth { get; set; } = null!;
    public DbSet<SourceRetrievalRun> SourceRetrievalRuns { get; set; } = null!;
    public DbSet<SourceRetrievalItem> SourceRetrievalItems { get; set; } = null!;
    public DbSet<SourceCitationCheck> SourceCitationChecks { get; set; } = null!;
    public DbSet<SourceQualityReport> SourceQualityReports { get; set; } = null!;
    public DbSet<TutorPedagogyEvaluationRun> TutorPedagogyEvaluationRuns { get; set; } = null!;
    public DbSet<TutorPedagogyEvaluationItem> TutorPedagogyEvaluationItems { get; set; } = null!;
    public DbSet<TutorPedagogyRubricScore> TutorPedagogyRubricScores { get; set; } = null!;
    public DbSet<TutorGoldenScenario> TutorGoldenScenarios { get; set; } = null!;
    public DbSet<TutorPedagogyFeedbackPatch> TutorPedagogyFeedbackPatches { get; set; } = null!;
    public DbSet<AssessmentCalibrationRun> AssessmentCalibrationRuns { get; set; } = null!;
    public DbSet<AssessmentCalibrationItem> AssessmentCalibrationItems { get; set; } = null!;
    public DbSet<AdaptiveAssessmentSession> AdaptiveAssessmentSessions { get; set; } = null!;
    public DbSet<AdaptiveAssessmentDecision> AdaptiveAssessmentDecisions { get; set; } = null!;
    public DbSet<TutorTraceProjection> TutorTraceProjections { get; set; } = null!;
    public DbSet<StandardsExportRun> StandardsExportRuns { get; set; } = null!;
    public DbSet<StandardsExportItem> StandardsExportItems { get; set; } = null!;
    public DbSet<StandardsValidationRun> StandardsValidationRuns { get; set; } = null!;
    public DbSet<StandardsValidationItem> StandardsValidationItems { get; set; } = null!;
    public DbSet<ExamDefinition> ExamDefinitions { get; set; } = null!;
    public DbSet<ExamVariant> ExamVariants { get; set; } = null!;
    public DbSet<ExamSection> ExamSections { get; set; } = null!;
    public DbSet<ExamSubject> ExamSubjects { get; set; } = null!;
    public DbSet<ExamTopic> ExamTopics { get; set; } = null!;
    public DbSet<ExamOutcome> ExamOutcomes { get; set; } = null!;
    public DbSet<ExamScoringRule> ExamScoringRules { get; set; } = null!;
    public DbSet<ExamTimeRule> ExamTimeRules { get; set; } = null!;
    public DbSet<ExamContentPack> ExamContentPacks { get; set; } = null!;
    public DbSet<QuestionItem> QuestionItems { get; set; } = null!;
    public DbSet<QuestionOption> QuestionOptions { get; set; } = null!;
    public DbSet<QuestionExplanation> QuestionExplanations { get; set; } = null!;
    public DbSet<QuestionTag> QuestionTags { get; set; } = null!;
    public DbSet<QuestionOutcomeLink> QuestionOutcomeLinks { get; set; } = null!;
    public DbSet<QuestionImportPreview> QuestionImportPreviews { get; set; } = null!;
    public DbSet<QuestionImportPreviewItem> QuestionImportPreviewItems { get; set; } = null!;
    public DbSet<CentralExamPracticeAttempt> CentralExamPracticeAttempts { get; set; } = null!;
    public DbSet<CentralExamPracticeAnswer> CentralExamPracticeAnswers { get; set; } = null!;
    public DbSet<CentralExamDenemeBlueprint> CentralExamDenemeBlueprints { get; set; } = null!;
    public DbSet<CentralExamDenemeBlueprintSection> CentralExamDenemeBlueprintSections { get; set; } = null!;
    public DbSet<CentralExamDenemeAttempt> CentralExamDenemeAttempts { get; set; } = null!;
    public DbSet<CentralExamDenemeAnswer> CentralExamDenemeAnswers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .Property(u => u.Plan)
            .HasConversion<string>();

        modelBuilder.Entity<Message>()
            .Property(m => m.MessageType)
            .HasConversion<string>();

        modelBuilder.Entity<Topic>()
            .Property(t => t.CurrentPhase)
            .HasConversion<string>();

        modelBuilder.Entity<Session>()
            .Property(s => s.CurrentState)
            .HasConversion<string>();

        modelBuilder.Entity<WikiBlock>()
            .Property(b => b.BlockType)
            .HasConversion<string>();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();
            
        modelBuilder.Entity<RefreshToken>()
            .Property(rt => rt.TokenHash)
            .HasMaxLength(64);

        modelBuilder.Entity<RefreshToken>()
            .Property(rt => rt.ReplacedByTokenHash)
            .HasMaxLength(64);

        modelBuilder.Entity<RefreshToken>()
            .Property(rt => rt.RevokedReason)
            .HasMaxLength(64);

        modelBuilder.Entity<RefreshToken>()
            .Property(rt => rt.RowVersion)
            .HasMaxLength(16)
            .IsConcurrencyToken();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.TokenHash)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => new { rt.UserId, rt.TokenFamilyId });

        modelBuilder.Entity<ToolTelemetryEvent>()
            .Property(t => t.ToolId)
            .HasMaxLength(128);

        modelBuilder.Entity<ToolTelemetryEvent>()
            .Property(t => t.CapabilityStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<ToolTelemetryEvent>()
            .Property(t => t.Provider)
            .HasMaxLength(128);

        modelBuilder.Entity<ToolTelemetryEvent>()
            .Property(t => t.Model)
            .HasMaxLength(256);

        modelBuilder.Entity<ToolTelemetryEvent>()
            .Property(t => t.ErrorCode)
            .HasMaxLength(128);

        modelBuilder.Entity<ToolTelemetryEvent>()
            .Property(t => t.CorrelationId)
            .HasMaxLength(128);

        modelBuilder.Entity<ToolTelemetryEvent>()
            .Property(t => t.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<ToolTelemetryEvent>()
            .HasIndex(t => new { t.ToolId, t.OccurredAt });

        modelBuilder.Entity<ToolTelemetryEvent>()
            .HasIndex(t => new { t.UserId, t.OccurredAt });

        modelBuilder.Entity<CostRecord>()
            .Property(c => c.AgentRole)
            .HasMaxLength(128);

        modelBuilder.Entity<CostRecord>()
            .Property(c => c.Provider)
            .HasMaxLength(128);

        modelBuilder.Entity<CostRecord>()
            .Property(c => c.Model)
            .HasMaxLength(256);

        modelBuilder.Entity<CostRecord>()
            .Property(c => c.EstimatedCostUsd)
            .HasPrecision(18, 8);

        modelBuilder.Entity<CostRecord>()
            .Property(c => c.ErrorCode)
            .HasMaxLength(128);

        modelBuilder.Entity<CostRecord>()
            .Property(c => c.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<CostRecord>()
            .HasIndex(c => new { c.UserId, c.OccurredAt });

        modelBuilder.Entity<CostRecord>()
            .HasIndex(c => new { c.TopicId, c.OccurredAt });

        modelBuilder.Entity<CostRecord>()
            .HasIndex(c => new { c.Provider, c.Model, c.OccurredAt });

        modelBuilder.Entity<AgentEvaluation>()
            .Property(ae => ae.AgentRole)
            .HasMaxLength(128);

        modelBuilder.Entity<AgentEvaluation>()
            .HasIndex(ae => new { ae.MessageId, ae.AgentRole })
            .IsUnique();

        modelBuilder.Entity<Session>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<WikiPage>()
            .HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizAttempt>()
            .HasOne(qa => qa.User)
            .WithMany()
            .HasForeignKey(qa => qa.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizAttempt>()
            .HasOne(qa => qa.QuizRun)
            .WithMany(qr => qr.Attempts)
            .HasForeignKey(qa => qa.QuizRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizAttempt>()
            .HasOne(qa => qa.AssessmentItem)
            .WithMany()
            .HasForeignKey(qa => qa.AssessmentItemId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        // QuizAttempt FK ilişkileri — nullable (serbest sohbette SessionId/TopicId null olabilir)
        modelBuilder.Entity<QuizAttempt>()
            .HasOne(qa => qa.Session)
            .WithMany()
            .HasForeignKey(qa => qa.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizAttempt>()
            .HasOne(qa => qa.Topic)
            .WithMany()
            .HasForeignKey(qa => qa.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizAttempt>()
            .HasIndex(qa => new { qa.UserId, qa.TopicId, qa.QuestionHash });

        modelBuilder.Entity<QuizAttempt>()
            .Property(qa => qa.QuestionHash)
            .HasMaxLength(450);

        modelBuilder.Entity<QuizAttempt>()
            .Property(qa => qa.SkillTag)
            .HasMaxLength(450);

        modelBuilder.Entity<QuizAttempt>()
            .Property(qa => qa.ConfidenceSelfRating)
            .HasPrecision(6, 4);

        modelBuilder.Entity<QuizAttempt>()
            .HasIndex(qa => new { qa.UserId, qa.TopicId, qa.SkillTag });

        modelBuilder.Entity<QuizAttempt>()
            .HasIndex(qa => new { qa.UserId, qa.AssessmentItemId });

        modelBuilder.Entity<QuizRun>()
            .HasOne(qr => qr.User)
            .WithMany()
            .HasForeignKey(qr => qr.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizRun>()
            .HasOne(qr => qr.Topic)
            .WithMany()
            .HasForeignKey(qr => qr.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizRun>()
            .HasOne(qr => qr.Session)
            .WithMany()
            .HasForeignKey(qr => qr.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuizRun>()
            .HasIndex(qr => new { qr.UserId, qr.TopicId, qr.CreatedAt });

        modelBuilder.Entity<LearningSignal>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningSignal>()
            .Property(s => s.SignalType)
            .HasMaxLength(450);

        modelBuilder.Entity<LearningSignal>()
            .HasOne(s => s.Topic)
            .WithMany()
            .HasForeignKey(s => s.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningSignal>()
            .HasOne(s => s.Session)
            .WithMany()
            .HasForeignKey(s => s.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningSignal>()
            .HasOne(s => s.QuizAttempt)
            .WithMany()
            .HasForeignKey(s => s.QuizAttemptId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningSignal>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.SignalType, s.CreatedAt });

        modelBuilder.Entity<ConceptGraphSnapshot>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptGraphSnapshot>()
            .HasOne(s => s.Topic)
            .WithMany()
            .HasForeignKey(s => s.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptGraphSnapshot>()
            .Property(s => s.IntentHash)
            .HasMaxLength(64);

        modelBuilder.Entity<ConceptGraphSnapshot>()
            .Property(s => s.SourceBundleHash)
            .HasMaxLength(64);

        modelBuilder.Entity<ConceptGraphSnapshot>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.IntentHash, s.CreatedAt });

        modelBuilder.Entity<ConceptGraphSnapshot>()
            .HasIndex(s => s.PlanRequestId);

        modelBuilder.Entity<LearningConcept>()
            .HasOne(c => c.ConceptGraphSnapshot)
            .WithMany(s => s.Concepts)
            .HasForeignKey(c => c.ConceptGraphSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LearningConcept>()
            .Property(c => c.StableKey)
            .HasMaxLength(450);

        modelBuilder.Entity<LearningConcept>()
            .HasIndex(c => new { c.ConceptGraphSnapshotId, c.StableKey })
            .IsUnique();

        modelBuilder.Entity<ConceptRelation>()
            .HasOne(r => r.ConceptGraphSnapshot)
            .WithMany(s => s.Relations)
            .HasForeignKey(r => r.ConceptGraphSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConceptRelation>()
            .Property(r => r.SourceConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<ConceptRelation>()
            .Property(r => r.TargetConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<ConceptRelation>()
            .HasIndex(r => new { r.ConceptGraphSnapshotId, r.SourceConceptKey, r.TargetConceptKey, r.RelationType });

        modelBuilder.Entity<LearningOutcome>()
            .HasOne(o => o.ConceptGraphSnapshot)
            .WithMany(s => s.Outcomes)
            .HasForeignKey(o => o.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LearningOutcome>()
            .Property(o => o.StableKey)
            .HasMaxLength(450);

        modelBuilder.Entity<LearningOutcome>()
            .HasIndex(o => new { o.ConceptGraphSnapshotId, o.StableKey });

        modelBuilder.Entity<OutcomeAlignment>()
            .HasOne(a => a.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(a => a.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<OutcomeAlignment>()
            .HasOne(a => a.LearningOutcome)
            .WithMany()
            .HasForeignKey(a => a.LearningOutcomeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<OutcomeAlignment>()
            .Property(a => a.EntityType)
            .HasMaxLength(128);

        modelBuilder.Entity<OutcomeAlignment>()
            .Property(a => a.EntityKey)
            .HasMaxLength(450);

        modelBuilder.Entity<OutcomeAlignment>()
            .HasIndex(a => new { a.EntityType, a.EntityId });

        modelBuilder.Entity<OutcomeAlignment>()
            .HasIndex(a => new { a.EntityType, a.EntityKey });

        modelBuilder.Entity<AssessmentItem>()
            .HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItem>()
            .HasOne(i => i.Topic)
            .WithMany()
            .HasForeignKey(i => i.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItem>()
            .HasOne(i => i.QuizRun)
            .WithMany()
            .HasForeignKey(i => i.QuizRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItem>()
            .HasOne(i => i.ConceptGraphSnapshot)
            .WithMany(s => s.AssessmentItems)
            .HasForeignKey(i => i.ConceptGraphSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItem>()
            .HasOne(i => i.LearningConcept)
            .WithMany()
            .HasForeignKey(i => i.LearningConceptId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItem>()
            .Property(i => i.AssessmentItemKey)
            .HasMaxLength(450);

        modelBuilder.Entity<AssessmentItem>()
            .Property(i => i.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<AssessmentItem>()
            .HasIndex(i => new { i.UserId, i.PlanRequestId, i.Order });

        modelBuilder.Entity<AssessmentItem>()
            .HasIndex(i => new { i.UserId, i.TopicId, i.ConceptKey });

        modelBuilder.Entity<DiagnosticProfile>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DiagnosticProfile>()
            .HasOne(p => p.Topic)
            .WithMany()
            .HasForeignKey(p => p.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DiagnosticProfile>()
            .HasOne(p => p.QuizRun)
            .WithMany()
            .HasForeignKey(p => p.QuizRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DiagnosticProfile>()
            .HasOne(p => p.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(p => p.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DiagnosticProfile>()
            .HasIndex(p => new { p.UserId, p.TopicId, p.CreatedAt });

        modelBuilder.Entity<DiagnosticProfile>()
            .HasIndex(p => new { p.UserId, p.PlanRequestId });

        modelBuilder.Entity<ConceptMastery>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptMastery>()
            .HasOne(m => m.Topic)
            .WithMany()
            .HasForeignKey(m => m.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptMastery>()
            .HasOne(m => m.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(m => m.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptMastery>()
            .Property(m => m.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<ConceptMastery>()
            .Property(m => m.MasteryScore)
            .HasPrecision(5, 2);

        modelBuilder.Entity<ConceptMastery>()
            .Property(m => m.Confidence)
            .HasPrecision(5, 2);

        modelBuilder.Entity<ConceptMastery>()
            .HasIndex(m => new { m.UserId, m.TopicId, m.ConceptKey })
            .IsUnique();

        modelBuilder.Entity<LearningEvent>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEvent>()
            .HasOne(e => e.Topic)
            .WithMany()
            .HasForeignKey(e => e.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEvent>()
            .HasOne(e => e.Session)
            .WithMany()
            .HasForeignKey(e => e.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEvent>()
            .HasOne(e => e.QuizAttempt)
            .WithMany()
            .HasForeignKey(e => e.QuizAttemptId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEvent>()
            .HasOne(e => e.AssessmentItem)
            .WithMany()
            .HasForeignKey(e => e.AssessmentItemId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEvent>()
            .Property(e => e.EventType)
            .HasMaxLength(128);

        modelBuilder.Entity<LearningEvent>()
            .Property(e => e.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<LearningEvent>()
            .HasIndex(e => new { e.UserId, e.TopicId, e.EventType, e.OccurredAt });

        modelBuilder.Entity<LearningEvent>()
            .HasIndex(e => new { e.UserId, e.ConceptKey, e.OccurredAt });

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .HasOne(q => q.User)
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .HasOne(q => q.Topic)
            .WithMany()
            .HasForeignKey(q => q.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .HasOne(q => q.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(q => q.ConceptGraphSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .Property(q => q.QualityStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .Property(q => q.FailuresJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .Property(q => q.DuplicateRatio)
            .HasPrecision(6, 4);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .Property(q => q.OutcomeCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .Property(q => q.MisconceptionCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .Property(q => q.SourceEvidenceRatio)
            .HasPrecision(6, 4);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .Property(q => q.RelationDensity)
            .HasPrecision(8, 6);

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .HasIndex(q => new { q.UserId, q.TopicId, q.CreatedAt });

        modelBuilder.Entity<ConceptGraphQualityRun>()
            .HasIndex(q => q.ConceptGraphSnapshotId);

        modelBuilder.Entity<AssessmentQualityRun>()
            .HasOne(q => q.User)
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentQualityRun>()
            .HasOne(q => q.Topic)
            .WithMany()
            .HasForeignKey(q => q.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentQualityRun>()
            .HasOne(q => q.QuizRun)
            .WithMany()
            .HasForeignKey(q => q.QuizRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentQualityRun>()
            .HasOne(q => q.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(q => q.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentQualityRun>()
            .Property(q => q.QualityStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<AssessmentQualityRun>()
            .Property(q => q.FailuresJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<AssessmentQualityRun>()
            .Property(q => q.ConceptCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentQualityRun>()
            .Property(q => q.LearningOutcomeCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentQualityRun>()
            .Property(q => q.MisconceptionTargetingRatio)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentQualityRun>()
            .Property(q => q.OptionQualityRatio)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentQualityRun>()
            .Property(q => q.ScoringRulePresenceRatio)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentQualityRun>()
            .HasIndex(q => new { q.UserId, q.TopicId, q.CreatedAt });

        modelBuilder.Entity<AssessmentQualityRun>()
            .HasIndex(q => q.AssessmentDraftId);

        modelBuilder.Entity<AssessmentItemStat>()
            .HasOne(s => s.AssessmentItem)
            .WithMany()
            .HasForeignKey(s => s.AssessmentItemId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItemStat>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItemStat>()
            .HasOne(s => s.Topic)
            .WithMany()
            .HasForeignKey(s => s.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItemStat>()
            .HasOne(s => s.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(s => s.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.QualityStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.CorrectRate)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.DiscriminationProxy)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.TotalTimeSeconds)
            .HasPrecision(12, 2);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.LastResponseTimeSeconds)
            .HasPrecision(10, 2);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.AverageTimeSeconds)
            .HasPrecision(10, 2);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.SkipRate)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.DifficultyEstimate)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.DiscriminationEstimate)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentItemStat>()
            .Property(s => s.CalibrationStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<AssessmentItemStat>()
            .HasIndex(s => s.AssessmentItemId)
            .IsUnique();

        modelBuilder.Entity<KnowledgeTracingState>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<KnowledgeTracingState>()
            .HasOne(s => s.Topic)
            .WithMany()
            .HasForeignKey(s => s.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<KnowledgeTracingState>()
            .HasOne(s => s.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(s => s.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.MasteryProbability)
            .HasPrecision(6, 4);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.Confidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.PriorMastery)
            .HasPrecision(6, 4);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.LearnRate)
            .HasPrecision(6, 4);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.Slip)
            .HasPrecision(6, 4);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.Guess)
            .HasPrecision(6, 4);

        modelBuilder.Entity<KnowledgeTracingState>()
            .Property(s => s.Decay)
            .HasPrecision(6, 4);

        modelBuilder.Entity<KnowledgeTracingState>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.ConceptKey })
            .IsUnique();

        modelBuilder.Entity<TutorPolicyTrace>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TutorPolicyTrace>()
            .HasOne(t => t.Topic)
            .WithMany()
            .HasForeignKey(t => t.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TutorPolicyTrace>()
            .HasOne(t => t.Session)
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TutorPolicyTrace>()
            .HasOne(t => t.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(t => t.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TutorPolicyTrace>()
            .Property(t => t.ActiveConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<TutorPolicyTrace>()
            .Property(t => t.PolicyViolationsJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPolicyTrace>()
            .HasIndex(t => new { t.UserId, t.TopicId, t.CreatedAt });

        modelBuilder.Entity<TutorPolicyTrace>()
            .HasIndex(t => new { t.UserId, t.SessionId, t.CreatedAt });

        modelBuilder.Entity<LearningEventSchemaViolation>()
            .HasOne(v => v.LearningEvent)
            .WithMany()
            .HasForeignKey(v => v.LearningEventId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEventSchemaViolation>()
            .HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEventSchemaViolation>()
            .HasOne(v => v.Topic)
            .WithMany()
            .HasForeignKey(v => v.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningEventSchemaViolation>()
            .Property(v => v.EventType)
            .HasMaxLength(128);

        modelBuilder.Entity<LearningEventSchemaViolation>()
            .Property(v => v.ViolationCode)
            .HasMaxLength(128);

        modelBuilder.Entity<LearningEventSchemaViolation>()
            .Property(v => v.PayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<LearningEventSchemaViolation>()
            .HasIndex(v => new { v.UserId, v.TopicId, v.CreatedAt });

        modelBuilder.Entity<ResourceConceptAlignment>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ResourceConceptAlignment>()
            .HasOne(a => a.Topic)
            .WithMany()
            .HasForeignKey(a => a.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ResourceConceptAlignment>()
            .HasOne(a => a.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(a => a.ConceptGraphSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ResourceConceptAlignment>()
            .Property(a => a.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<ResourceConceptAlignment>()
            .Property(a => a.OutcomeKey)
            .HasMaxLength(450);

        modelBuilder.Entity<ResourceConceptAlignment>()
            .Property(a => a.AlignmentScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<ResourceConceptAlignment>()
            .HasIndex(a => new { a.UserId, a.TopicId, a.CreatedAt });

        modelBuilder.Entity<ResourceConceptAlignment>()
            .HasIndex(a => new { a.ConceptGraphSnapshotId, a.ConceptKey });

        modelBuilder.Entity<LearningQualityReport>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningQualityReport>()
            .HasOne(r => r.Topic)
            .WithMany()
            .HasForeignKey(r => r.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningQualityReport>()
            .HasOne(r => r.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(r => r.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.ReportJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.TutorPedagogyScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.AssessmentCalibrationStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.AdaptiveReadiness)
            .HasMaxLength(64);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.ItemBankHealth)
            .HasMaxLength(64);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.TraceHealth)
            .HasMaxLength(64);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.StandardsAlignmentStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.CaseLikeCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.QtiLikeCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<LearningQualityReport>()
            .Property(r => r.CaliperXapiCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<LearningQualityReport>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.GeneratedAt });

        modelBuilder.Entity<TutorWorkingMemorySnapshot>()
            .Property(s => s.ActiveConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<TutorWorkingMemorySnapshot>()
            .Property(s => s.SnapshotJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorWorkingMemorySnapshot>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.CreatedAt });

        modelBuilder.Entity<TutorWorkingMemorySnapshot>()
            .HasIndex(s => new { s.UserId, s.SessionId, s.CreatedAt });

        modelBuilder.Entity<TutorTurnState>()
            .Property(s => s.ActiveConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<TutorTurnState>()
            .Property(s => s.UserMessageHash)
            .HasMaxLength(128);

        modelBuilder.Entity<TutorTurnState>()
            .Property(s => s.StateJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorTurnState>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.CreatedAt });

        modelBuilder.Entity<TutorTurnState>()
            .HasIndex(s => new { s.UserId, s.SessionId, s.CreatedAt });

        modelBuilder.Entity<TutorMemoryPatch>()
            .Property(p => p.PatchJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorMemoryPatch>()
            .HasIndex(p => new { p.UserId, p.TopicId, p.CreatedAt });

        modelBuilder.Entity<LearnerProfile>()
            .Property(p => p.StyleConfidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<LearnerProfile>()
            .Property(p => p.ProfileJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<LearnerProfile>()
            .HasIndex(p => new { p.UserId, p.TopicId, p.UpdatedAt });

        modelBuilder.Entity<LearningStyleSignal>()
            .Property(s => s.Weight)
            .HasPrecision(6, 4);

        modelBuilder.Entity<LearningStyleSignal>()
            .Property(s => s.Confidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<LearningStyleSignal>()
            .Property(s => s.PayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<LearningStyleSignal>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.CreatedAt });

        modelBuilder.Entity<AffectiveSignal>()
            .Property(s => s.Confidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AffectiveSignal>()
            .Property(s => s.PayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<AffectiveSignal>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.CreatedAt });

        modelBuilder.Entity<CognitiveLoadSignal>()
            .Property(s => s.Confidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<CognitiveLoadSignal>()
            .Property(s => s.PayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<CognitiveLoadSignal>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.CreatedAt });

        modelBuilder.Entity<TutorActionTrace>()
            .Property(t => t.ActiveConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<TutorActionTrace>()
            .Property(t => t.ToolPlanJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorActionTrace>()
            .Property(t => t.ArtifactPlanJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorActionTrace>()
            .HasIndex(t => new { t.UserId, t.TopicId, t.CreatedAt });

        modelBuilder.Entity<TutorActionTrace>()
            .HasIndex(t => new { t.UserId, t.SessionId, t.CreatedAt });

        modelBuilder.Entity<TutorToolCall>()
            .Property(t => t.ResultJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorToolCall>()
            .Property(t => t.Provider)
            .HasMaxLength(128);

        modelBuilder.Entity<TutorToolCall>()
            .Property(t => t.ErrorCode)
            .HasMaxLength(128);

        modelBuilder.Entity<TutorToolCall>()
            .Property(t => t.SafeMessage)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorToolCall>()
            .HasIndex(t => new { t.UserId, t.SessionId, t.CreatedAt });

        modelBuilder.Entity<TeachingArtifact>()
            .Property(a => a.Content)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingArtifact>()
            .Property(a => a.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingArtifact>()
            .Property(a => a.ExternalUrl)
            .HasMaxLength(2048);

        modelBuilder.Entity<TeachingArtifact>()
            .Property(a => a.RenderError)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingArtifact>()
            .HasIndex(a => new { a.UserId, a.TopicId, a.CreatedAt });

        modelBuilder.Entity<TutorReflectionUpdate>()
            .Property(r => r.ReflectionJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorReflectionUpdate>()
            .HasIndex(r => new { r.UserId, r.SessionId, r.CreatedAt });

        modelBuilder.Entity<TutorPolicyViolationV2>()
            .HasIndex(v => new { v.UserId, v.SessionId, v.CreatedAt });

        modelBuilder.Entity<TutorPedagogyEvaluationRun>()
            .Property(e => e.OverallScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<TutorPedagogyEvaluationRun>()
            .Property(e => e.RunJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyEvaluationRun>()
            .Property(e => e.Summary)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyEvaluationRun>()
            .Property(e => e.Recommendation)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyEvaluationRun>()
            .HasIndex(e => new { e.UserId, e.TopicId, e.CreatedAt });

        modelBuilder.Entity<TutorPedagogyEvaluationRun>()
            .HasIndex(e => new { e.UserId, e.SessionId, e.CreatedAt });

        modelBuilder.Entity<TutorPedagogyEvaluationItem>()
            .Property(e => e.UserMessage)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyEvaluationItem>()
            .Property(e => e.AssistantAnswer)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyEvaluationItem>()
            .Property(e => e.ItemJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyEvaluationItem>()
            .HasOne(e => e.Run)
            .WithMany()
            .HasForeignKey(e => e.EvaluationRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TutorPedagogyRubricScore>()
            .Property(e => e.Score)
            .HasPrecision(6, 4);

        modelBuilder.Entity<TutorPedagogyRubricScore>()
            .Property(e => e.Evidence)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyRubricScore>()
            .Property(e => e.Recommendation)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyRubricScore>()
            .HasIndex(e => new { e.UserId, e.TopicId, e.CreatedAt });

        modelBuilder.Entity<TutorPedagogyRubricScore>()
            .HasOne(e => e.Run)
            .WithMany()
            .HasForeignKey(e => e.EvaluationRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TutorGoldenScenario>()
            .HasIndex(e => e.ScenarioKey)
            .IsUnique();

        modelBuilder.Entity<TutorGoldenScenario>()
            .Property(e => e.RequiredRubricsJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyFeedbackPatch>()
            .Property(e => e.Feedback)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyFeedbackPatch>()
            .Property(e => e.PatchJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorPedagogyFeedbackPatch>()
            .HasIndex(e => new { e.UserId, e.TopicId, e.CreatedAt });

        modelBuilder.Entity<TutorMemoryFragment>()
            .Property(f => f.Content)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorMemoryFragment>()
            .Property(f => f.EmbeddingJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorMemoryFragment>()
            .Property(f => f.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<TutorMemoryFragment>()
            .Property(f => f.Importance)
            .HasPrecision(6, 4);

        modelBuilder.Entity<TutorMemoryFragment>()
            .HasIndex(f => new { f.UserId, f.TopicId, f.CreatedAt });

        modelBuilder.Entity<TutorMemoryFragment>()
            .HasIndex(f => new { f.UserId, f.FragmentType, f.CreatedAt });

        modelBuilder.Entity<RagEvaluationRun>()
            .Property(r => r.ReportJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<RagEvaluationRun>()
            .Property(r => r.FaithfulnessScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<RagEvaluationRun>()
            .Property(r => r.ContextRelevanceScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<RagEvaluationRun>()
            .Property(r => r.AnswerRelevanceScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<RagEvaluationRun>()
            .Property(r => r.CitationCoverageScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<RagEvaluationRun>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.CreatedAt });

        modelBuilder.Entity<RagEvaluationItem>()
            .HasOne(i => i.Run)
            .WithMany()
            .HasForeignKey(i => i.RagEvaluationRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.Query)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.Answer)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.ContextJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.Notes)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.FaithfulnessScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.ContextRelevanceScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.AnswerRelevanceScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<RagEvaluationItem>()
            .Property(i => i.CitationCoverageScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.Provider)
            .HasMaxLength(128);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.EvidenceType)
            .HasMaxLength(96);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.Query)
            .HasMaxLength(900);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.Title)
            .HasMaxLength(900);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.Summary)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.FactualClaim)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.AnalogyCandidate)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.ClassroomUse)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.CitationUrl)
            .HasMaxLength(2048);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.Confidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<TeachingEvidenceItem>()
            .Property(e => e.RawPayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingEvidenceItem>()
            .HasIndex(e => new { e.UserId, e.TopicId, e.CreatedAt });

        modelBuilder.Entity<TeachingEvidenceItem>()
            .HasIndex(e => new { e.UserId, e.TutorActionTraceId, e.CreatedAt });

        modelBuilder.Entity<TeachingEvidenceItem>()
            .HasIndex(e => new { e.EvidenceType, e.Provider, e.RawPayloadHash });

        modelBuilder.Entity<TeachingEvidenceProviderHealth>()
            .Property(h => h.Provider)
            .HasMaxLength(128);

        modelBuilder.Entity<TeachingEvidenceProviderHealth>()
            .Property(h => h.EvidenceType)
            .HasMaxLength(96);

        modelBuilder.Entity<TeachingEvidenceProviderHealth>()
            .Property(h => h.LastErrorCode)
            .HasMaxLength(128);

        modelBuilder.Entity<TeachingEvidenceProviderHealth>()
            .Property(h => h.Notes)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TeachingEvidenceProviderHealth>()
            .HasIndex(h => new { h.Provider, h.EvidenceType, h.CheckedAt });

        modelBuilder.Entity<SourceRetrievalRun>()
            .Property(r => r.Query)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceRetrievalRun>()
            .Property(r => r.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceRetrievalRun>()
            .Property(r => r.MaxScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceRetrievalRun>()
            .Property(r => r.AverageScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceRetrievalRun>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.CreatedAt });

        modelBuilder.Entity<SourceRetrievalRun>()
            .HasIndex(r => new { r.UserId, r.SourceId, r.CreatedAt });

        modelBuilder.Entity<SourceRetrievalRun>()
            .HasOne(r => r.Source)
            .WithMany()
            .HasForeignKey(r => r.SourceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SourceRetrievalItem>()
            .Property(i => i.EmbeddingScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceRetrievalItem>()
            .Property(i => i.LexicalScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceRetrievalItem>()
            .Property(i => i.FusedScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceRetrievalItem>()
            .Property(i => i.Snippet)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceRetrievalItem>()
            .HasIndex(i => new { i.SourceRetrievalRunId, i.Rank });

        modelBuilder.Entity<SourceRetrievalItem>()
            .HasOne(i => i.Source)
            .WithMany()
            .HasForeignKey(i => i.SourceId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SourceRetrievalItem>()
            .HasOne(i => i.SourceChunk)
            .WithMany()
            .HasForeignKey(i => i.SourceChunkId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SourceCitationCheck>()
            .Property(c => c.Answer)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceCitationCheck>()
            .Property(c => c.ClaimText)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceCitationCheck>()
            .Property(c => c.Confidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceCitationCheck>()
            .HasIndex(c => new { c.UserId, c.TopicId, c.CreatedAt });

        modelBuilder.Entity<SourceCitationCheck>()
            .HasIndex(c => new { c.SourceRetrievalRunId, c.CheckStatus });

        modelBuilder.Entity<SourceCitationCheck>()
            .HasOne(c => c.RetrievalRun)
            .WithMany()
            .HasForeignKey(c => c.SourceRetrievalRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SourceCitationCheck>()
            .HasOne(c => c.Source)
            .WithMany()
            .HasForeignKey(c => c.SourceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SourceCitationCheck>()
            .HasOne(c => c.SourceChunk)
            .WithMany()
            .HasForeignKey(c => c.SourceChunkId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SourceQualityReport>()
            .Property(r => r.AverageContextRelevance)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceQualityReport>()
            .Property(r => r.CitationCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<SourceQualityReport>()
            .Property(r => r.ReportJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceQualityReport>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.GeneratedAt });

        modelBuilder.Entity<SourceQualityReport>()
            .HasOne(r => r.Source)
            .WithMany()
            .HasForeignKey(r => r.SourceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<RemediationPlan>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<RemediationPlan>()
            .HasOne(r => r.Topic)
            .WithMany()
            .HasForeignKey(r => r.TopicId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<RemediationPlan>()
            .HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<RemediationPlan>()
            .Property(r => r.LessonMarkdown)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<RemediationPlan>()
            .Property(r => r.MicroQuizJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<StudyRecommendation>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StudyRecommendation>()
            .HasOne(r => r.Topic)
            .WithMany()
            .HasForeignKey(r => r.TopicId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StudyRecommendation>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.IsDone });

        // AgentEvaluation FK ilişkileri -> NoAction (döngü engelleme)
        modelBuilder.Entity<AgentEvaluation>()
            .HasOne(ae => ae.User)
            .WithMany()
            .HasForeignKey(ae => ae.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AgentEvaluation>()
            .HasOne(ae => ae.Session)
            .WithMany()
            .HasForeignKey(ae => ae.SessionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AgentEvaluation>()
            .HasOne(ae => ae.Message)
            .WithMany()
            .HasForeignKey(ae => ae.MessageId)
            .OnDelete(DeleteBehavior.NoAction);

        // Topic self-referential hiyerarşi (Deep Plan)
        // SQL Server: cascade cycle'ı önlemek için NoAction (silme uygulama katmanında yönetilir)
        modelBuilder.Entity<Topic>()
            .HasOne(t => t.Parent)
            .WithMany(t => t.SubTopics)
            .HasForeignKey(t => t.ParentTopicId)
            .OnDelete(DeleteBehavior.NoAction);

        // SkillMastery FK ilişkileri — NoAction (cascade cycle engelleme)
        modelBuilder.Entity<SkillMastery>()
            .HasOne(sm => sm.User)
            .WithMany()
            .HasForeignKey(sm => sm.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SkillMastery>()
            .HasOne(sm => sm.Topic)
            .WithMany()
            .HasForeignKey(sm => sm.TopicId)
            .OnDelete(DeleteBehavior.NoAction);

        // SkillMastery: kullanıcı + konu bazlı mastery sorguları
        modelBuilder.Entity<SkillMastery>()
            .HasIndex(sm => new { sm.UserId, sm.TopicId });

        // WikiPage.Content → nvarchar(max) açık eşleme
        modelBuilder.Entity<WikiPage>()
            .Property(w => w.Content)
            .HasColumnType("nvarchar(max)");

        // Decimal precision
        modelBuilder.Entity<Session>()
            .Property(s => s.TotalCostUSD)
            .HasPrecision(10, 6);
        modelBuilder.Entity<Message>()
            .Property(m => m.CostUSD)
            .HasPrecision(10, 6);

        // ── Performance Indexes ───────────────────────────────────────────────
        // Topic: sidebar tree + quiz order sorguları
        modelBuilder.Entity<Topic>()
            .HasIndex(t => t.ParentTopicId);
        modelBuilder.Entity<Topic>()
            .HasIndex(t => new { t.UserId, t.Order });
        modelBuilder.Entity<Topic>()
            .HasIndex(t => new { t.UserId, t.PlanIntent });

        // Session: kullanıcı + konu bazlı oturum arama
        modelBuilder.Entity<Session>()
            .HasIndex(s => new { s.UserId, s.TopicId });

        // Message: oturum mesaj listesi + sıralama
        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.SessionId, m.CreatedAt });

        modelBuilder.Entity<Message>()
            .Property(m => m.MetadataJson)
            .HasColumnType("nvarchar(max)");

        // WikiPage: konu bazlı wiki içerik yükleme
        modelBuilder.Entity<WikiPage>()
            .HasIndex(w => w.TopicId);

        // NotebookLM sources: topic/session scoped documents with retrievable chunks
        modelBuilder.Entity<LearningSource>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningSource>()
            .HasOne(s => s.Topic)
            .WithMany()
            .HasForeignKey(s => s.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningSource>()
            .HasOne(s => s.Session)
            .WithMany()
            .HasForeignKey(s => s.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<LearningSource>()
            .Property(s => s.FileSizeBytes)
            .HasDefaultValue(0L);

        modelBuilder.Entity<LearningSource>()
            .HasIndex(s => new { s.UserId, s.TopicId });

        modelBuilder.Entity<LearningSource>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.IsDeleted });

        modelBuilder.Entity<LearningSource>()
            .HasIndex(s => new { s.UserId, s.SessionId });

        modelBuilder.Entity<SourceChunk>()
            .HasOne(c => c.LearningSource)
            .WithMany(s => s.Chunks)
            .HasForeignKey(c => c.LearningSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SourceChunk>()
            .Property(c => c.Text)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceChunk>()
            .Property(c => c.EmbeddingJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SourceChunk>()
            .HasIndex(c => new { c.LearningSourceId, c.PageNumber, c.ChunkIndex });

        modelBuilder.Entity<SourceChunk>()
            .HasIndex(c => new { c.LearningSourceId, c.IsDeleted, c.PageNumber, c.ChunkIndex });

        modelBuilder.Entity<AudioOverviewJob>()
            .HasOne(j => j.User)
            .WithMany()
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AudioOverviewJob>()
            .HasOne(j => j.Topic)
            .WithMany()
            .HasForeignKey(j => j.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AudioOverviewJob>()
            .HasOne(j => j.Session)
            .WithMany()
            .HasForeignKey(j => j.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AudioOverviewJob>()
            .Property(j => j.Script)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<AudioOverviewJob>()
            .HasIndex(j => new { j.UserId, j.TopicId, j.CreatedAt });

        modelBuilder.Entity<ClassroomSession>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ClassroomSession>()
            .HasOne(c => c.Topic)
            .WithMany()
            .HasForeignKey(c => c.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ClassroomSession>()
            .HasOne(c => c.Session)
            .WithMany()
            .HasForeignKey(c => c.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ClassroomSession>()
            .HasOne(c => c.AudioOverviewJob)
            .WithMany()
            .HasForeignKey(c => c.AudioOverviewJobId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ClassroomSession>()
            .Property(c => c.Transcript)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<ClassroomSession>()
            .HasIndex(c => new { c.UserId, c.TopicId, c.UpdatedAt });

        modelBuilder.Entity<ReviewItem>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ReviewItem>()
            .HasOne(r => r.Topic)
            .WithMany()
            .HasForeignKey(r => r.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ReviewItem>()
            .HasOne(r => r.QuizAttempt)
            .WithMany()
            .HasForeignKey(r => r.QuizAttemptId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ReviewItem>()
            .HasOne(r => r.LearningSignal)
            .WithMany()
            .HasForeignKey(r => r.LearningSignalId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ReviewItem>()
            .HasOne(r => r.Flashcard)
            .WithMany()
            .HasForeignKey(r => r.FlashcardId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ReviewItem>()
            .HasOne(r => r.RemediationPlan)
            .WithMany()
            .HasForeignKey(r => r.RemediationPlanId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ReviewItem>()
            .Property(r => r.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<ReviewItem>()
            .HasIndex(r => new { r.UserId, r.Status, r.DueAt });

        modelBuilder.Entity<ReviewItem>()
            .HasIndex(r => new { r.UserId, r.TopicId });

        modelBuilder.Entity<ReviewItem>()
            .HasIndex(r => new { r.UserId, r.ReviewKey })
            .IsUnique()
            .HasFilter("[Status] = 'active' AND [ReviewKey] <> 'topic:global:general'");

        modelBuilder.Entity<Flashcard>()
            .HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Flashcard>()
            .HasOne(f => f.Topic)
            .WithMany()
            .HasForeignKey(f => f.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Flashcard>()
            .HasOne(f => f.LearningSource)
            .WithMany()
            .HasForeignKey(f => f.LearningSourceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Flashcard>()
            .HasOne(f => f.WikiPage)
            .WithMany()
            .HasForeignKey(f => f.WikiPageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Flashcard>()
            .HasOne(f => f.QuizAttempt)
            .WithMany()
            .HasForeignKey(f => f.QuizAttemptId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Flashcard>()
            .Property(f => f.Front)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Flashcard>()
            .Property(f => f.Back)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Flashcard>()
            .Property(f => f.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Flashcard>()
            .HasIndex(f => new { f.UserId, f.TopicId, f.Status });

        modelBuilder.Entity<DailyChallenge>()
            .HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DailyChallenge>()
            .HasOne(d => d.Topic)
            .WithMany()
            .HasForeignKey(d => d.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DailyChallenge>()
            .HasOne(d => d.ReviewItem)
            .WithMany(r => r.DailyChallenges)
            .HasForeignKey(d => d.ReviewItemId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DailyChallenge>()
            .Property(d => d.QuestionsJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<DailyChallenge>()
            .Property(d => d.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<DailyChallenge>()
            .HasIndex(d => new { d.UserId, d.TopicId, d.Date })
            .IsUnique();

        modelBuilder.Entity<DailyChallengeSubmission>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DailyChallengeSubmission>()
            .HasOne(s => s.DailyChallenge)
            .WithMany(d => d.Submissions)
            .HasForeignKey(s => s.DailyChallengeId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DailyChallengeSubmission>()
            .HasOne(s => s.XpEvent)
            .WithMany()
            .HasForeignKey(s => s.XpEventId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DailyChallengeSubmission>()
            .HasIndex(s => new { s.UserId, s.DailyChallengeId })
            .IsUnique();

        modelBuilder.Entity<XpEvent>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<XpEvent>()
            .Property(e => e.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<XpEvent>()
            .HasIndex(e => new { e.UserId, e.EventKey })
            .IsUnique();

        modelBuilder.Entity<Badge>()
            .HasIndex(b => b.Code)
            .IsUnique();

        modelBuilder.Entity<Badge>()
            .Property(b => b.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<UserBadge>()
            .HasOne(ub => ub.User)
            .WithMany()
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<UserBadge>()
            .HasOne(ub => ub.Badge)
            .WithMany(b => b.UserBadges)
            .HasForeignKey(ub => ub.BadgeId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<UserBadge>()
            .HasOne(ub => ub.SourceEvent)
            .WithMany()
            .HasForeignKey(ub => ub.SourceEventId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<UserBadge>()
            .HasIndex(ub => new { ub.UserId, ub.BadgeId })
            .IsUnique();

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Notification>()
            .Property(n => n.Body)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Notification>()
            .Property(n => n.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.Status, n.CreatedAt });

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.Topic)
            .WithMany()
            .HasForeignKey(b => b.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.Session)
            .WithMany()
            .HasForeignKey(b => b.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.Message)
            .WithMany()
            .HasForeignKey(b => b.MessageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.LearningSource)
            .WithMany()
            .HasForeignKey(b => b.LearningSourceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.WikiPage)
            .WithMany()
            .HasForeignKey(b => b.WikiPageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.ReviewItem)
            .WithMany()
            .HasForeignKey(b => b.ReviewItemId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .HasOne(b => b.Flashcard)
            .WithMany()
            .HasForeignKey(b => b.FlashcardId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Bookmark>()
            .Property(b => b.Title)
            .HasMaxLength(256);

        modelBuilder.Entity<Bookmark>()
            .Property(b => b.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<Bookmark>()
            .Property(b => b.Note)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Bookmark>()
            .Property(b => b.Quote)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Bookmark>()
            .Property(b => b.TagsJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Bookmark>()
            .HasIndex(b => new { b.UserId, b.TopicId, b.Status, b.CreatedAt });

        modelBuilder.Entity<Bookmark>()
            .HasIndex(b => new { b.UserId, b.MessageId })
            .IsUnique()
            .HasFilter("[MessageId] IS NOT NULL AND [Status] = 'active'");

        modelBuilder.Entity<PushSubscription>()
            .Property(p => p.Endpoint)
            .HasMaxLength(450);

        modelBuilder.Entity<PushSubscription>()
            .Property(p => p.DeviceLabel)
            .HasMaxLength(160);

        modelBuilder.Entity<PushSubscription>()
            .Property(p => p.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<PushSubscription>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<PushSubscription>()
            .HasIndex(p => new { p.UserId, p.Status });

        modelBuilder.Entity<PushSubscription>()
            .HasIndex(p => new { p.UserId, p.Endpoint })
            .IsUnique()
            .HasFilter("[Status] = 'active'");

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .HasOne(r => r.Topic)
            .WithMany()
            .HasForeignKey(r => r.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .HasOne(r => r.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(r => r.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .Property(r => r.AverageDifficulty)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .Property(r => r.CalibrationStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .Property(r => r.AdaptiveReadiness)
            .HasMaxLength(64);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .Property(r => r.ItemBankHealth)
            .HasMaxLength(64);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .Property(r => r.AverageDiscrimination)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .Property(r => r.AverageExposure)
            .HasPrecision(10, 4);

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .Property(r => r.ReportJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<AssessmentCalibrationRun>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.CreatedAt });

        modelBuilder.Entity<AssessmentCalibrationItem>()
            .HasOne(i => i.Run)
            .WithMany()
            .HasForeignKey(i => i.AssessmentCalibrationRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AssessmentCalibrationItem>()
            .HasOne(i => i.AssessmentItem)
            .WithMany()
            .HasForeignKey(i => i.AssessmentItemId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssessmentCalibrationItem>()
            .Property(i => i.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<AssessmentCalibrationItem>()
            .Property(i => i.CalibrationStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<AssessmentCalibrationItem>()
            .Property(i => i.DifficultyEstimate)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentCalibrationItem>()
            .Property(i => i.DiscriminationEstimate)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AssessmentCalibrationItem>()
            .HasIndex(i => new { i.UserId, i.TopicId, i.ConceptKey });

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .HasOne(s => s.Topic)
            .WithMany()
            .HasForeignKey(s => s.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .HasOne(s => s.Session)
            .WithMany()
            .HasForeignKey(s => s.SessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .HasOne(s => s.QuizRun)
            .WithMany()
            .HasForeignKey(s => s.QuizRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .HasOne(s => s.ConceptGraphSnapshot)
            .WithMany()
            .HasForeignKey(s => s.ConceptGraphSnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .Property(s => s.TargetConceptsJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .Property(s => s.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .Property(s => s.StopReason)
            .HasMaxLength(128);

        modelBuilder.Entity<AdaptiveAssessmentSession>()
            .HasIndex(s => new { s.UserId, s.TopicId, s.CreatedAt });

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .HasOne(d => d.AdaptiveAssessmentSession)
            .WithMany()
            .HasForeignKey(d => d.AdaptiveAssessmentSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .HasOne(d => d.AssessmentItem)
            .WithMany()
            .HasForeignKey(d => d.AssessmentItemId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .HasOne(d => d.QuizAttempt)
            .WithMany()
            .HasForeignKey(d => d.QuizAttemptId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .Property(d => d.SelectionScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .Property(d => d.ConceptKey)
            .HasMaxLength(450);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .Property(d => d.MasteryProbability)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .Property(d => d.MasteryConfidence)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .Property(d => d.ItemQualityScore)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .Property(d => d.ExposurePenalty)
            .HasPrecision(6, 4);

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .Property(d => d.SelectedQuestionJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<AdaptiveAssessmentDecision>()
            .HasIndex(d => new { d.AdaptiveAssessmentSessionId, d.WasAnswered, d.CreatedAt });

        modelBuilder.Entity<TutorTraceProjection>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TutorTraceProjection>()
            .HasOne(p => p.Session)
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TutorTraceProjection>()
            .HasOne(p => p.Topic)
            .WithMany()
            .HasForeignKey(p => p.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TutorTraceProjection>()
            .Property(p => p.PayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<TutorTraceProjection>()
            .Property(p => p.EventType)
            .HasMaxLength(128);

        modelBuilder.Entity<TutorTraceProjection>()
            .Property(p => p.EventGroup)
            .HasMaxLength(64);

        modelBuilder.Entity<TutorTraceProjection>()
            .Property(p => p.Severity)
            .HasMaxLength(32);

        modelBuilder.Entity<TutorTraceProjection>()
            .HasIndex(p => new { p.SessionId, p.StreamId })
            .IsUnique();

        modelBuilder.Entity<TutorTraceProjection>()
            .HasIndex(p => new { p.UserId, p.SessionId, p.OccurredAt });

        modelBuilder.Entity<AudioOverviewJob>()
            .HasIndex(a => a.AudioExpiresAt);

        modelBuilder.Entity<ClassroomInteraction>()
            .HasIndex(c => c.AudioExpiresAt);

        modelBuilder.Entity<StandardsExportRun>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StandardsExportRun>()
            .Property(r => r.PayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<StandardsExportRun>()
            .Property(r => r.ExportType)
            .HasMaxLength(64);

        modelBuilder.Entity<StandardsExportRun>()
            .Property(r => r.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<StandardsExportRun>()
            .Property(r => r.CaseCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<StandardsExportRun>()
            .Property(r => r.QtiCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<StandardsExportRun>()
            .Property(r => r.CaliperXapiCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<StandardsExportRun>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.CreatedAt });

        modelBuilder.Entity<StandardsExportItem>()
            .HasOne(i => i.StandardsExportRun)
            .WithMany()
            .HasForeignKey(i => i.StandardsExportRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StandardsExportItem>()
            .Property(i => i.PayloadJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<StandardsExportItem>()
            .Property(i => i.StandardFamily)
            .HasMaxLength(64);

        modelBuilder.Entity<StandardsExportItem>()
            .Property(i => i.EntityType)
            .HasMaxLength(128);

        modelBuilder.Entity<StandardsExportItem>()
            .Property(i => i.EntityKey)
            .HasMaxLength(450);

        modelBuilder.Entity<StandardsExportItem>()
            .HasIndex(i => new { i.StandardsExportRunId, i.StandardFamily, i.EntityType });

        modelBuilder.Entity<StandardsValidationRun>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StandardsValidationRun>()
            .Property(r => r.SummaryJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<StandardsValidationRun>()
            .Property(r => r.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<StandardsValidationRun>()
            .Property(r => r.CaseCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<StandardsValidationRun>()
            .Property(r => r.QtiCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<StandardsValidationRun>()
            .Property(r => r.CaliperXapiCoverage)
            .HasPrecision(6, 4);

        modelBuilder.Entity<StandardsValidationRun>()
            .HasIndex(r => new { r.UserId, r.TopicId, r.CreatedAt });

        modelBuilder.Entity<StandardsValidationItem>()
            .HasOne(i => i.StandardsValidationRun)
            .WithMany()
            .HasForeignKey(i => i.StandardsValidationRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StandardsValidationItem>()
            .Property(i => i.DetailJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<StandardsValidationItem>()
            .Property(i => i.StandardFamily)
            .HasMaxLength(64);

        modelBuilder.Entity<StandardsValidationItem>()
            .Property(i => i.EntityType)
            .HasMaxLength(128);

        modelBuilder.Entity<StandardsValidationItem>()
            .Property(i => i.EntityKey)
            .HasMaxLength(450);

        modelBuilder.Entity<StandardsValidationItem>()
            .Property(i => i.Severity)
            .HasMaxLength(32);

        modelBuilder.Entity<StandardsValidationItem>()
            .Property(i => i.IssueCode)
            .HasMaxLength(128);

        modelBuilder.Entity<StandardsValidationItem>()
            .HasIndex(i => new { i.StandardsValidationRunId, i.StandardFamily, i.Severity });

        modelBuilder.Entity<ExamDefinition>()
            .HasOne(e => e.OwnerUser)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.ExamFamily)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.Visibility)
            .HasMaxLength(32);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.VerificationStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.SourceTitle)
            .HasMaxLength(512);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.SourceUrl)
            .HasMaxLength(1024);

        modelBuilder.Entity<ExamDefinition>()
            .Property(e => e.VerifiedBy)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamDefinition>()
            .HasIndex(e => new { e.OwnerUserId, e.Code, e.IsDeleted });

        modelBuilder.Entity<ExamDefinition>()
            .HasIndex(e => new { e.Code, e.Visibility, e.IsDeleted });

        modelBuilder.Entity<ExamVariant>()
            .HasOne(v => v.ExamDefinition)
            .WithMany(d => d.Variants)
            .HasForeignKey(v => v.ExamDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamVariant>()
            .Property(v => v.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamVariant>()
            .Property(v => v.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamVariant>()
            .HasIndex(v => new { v.ExamDefinitionId, v.Code, v.IsDeleted });

        modelBuilder.Entity<ExamSection>()
            .HasOne(s => s.ExamVariant)
            .WithMany(v => v.Sections)
            .HasForeignKey(s => s.ExamVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamSection>()
            .Property(s => s.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamSection>()
            .Property(s => s.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamSection>()
            .HasIndex(s => new { s.ExamVariantId, s.Code, s.IsDeleted });

        modelBuilder.Entity<ExamSubject>()
            .HasOne(s => s.ExamSection)
            .WithMany(section => section.Subjects)
            .HasForeignKey(s => s.ExamSectionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamSubject>()
            .Property(s => s.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamSubject>()
            .Property(s => s.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamSubject>()
            .HasIndex(s => new { s.ExamSectionId, s.Code, s.IsDeleted });

        modelBuilder.Entity<ExamTopic>()
            .HasOne(t => t.ExamSubject)
            .WithMany(s => s.Topics)
            .HasForeignKey(t => t.ExamSubjectId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamTopic>()
            .HasOne(t => t.ParentExamTopic)
            .WithMany(t => t.Children)
            .HasForeignKey(t => t.ParentExamTopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamTopic>()
            .Property(t => t.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamTopic>()
            .Property(t => t.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamTopic>()
            .HasIndex(t => new { t.ExamSubjectId, t.ParentExamTopicId, t.Code, t.IsDeleted });

        modelBuilder.Entity<ExamOutcome>()
            .HasOne(o => o.ExamTopic)
            .WithMany(t => t.Outcomes)
            .HasForeignKey(o => o.ExamTopicId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamOutcome>()
            .Property(o => o.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamOutcome>()
            .Property(o => o.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamOutcome>()
            .HasIndex(o => new { o.ExamTopicId, o.Code, o.IsDeleted });

        modelBuilder.Entity<ExamScoringRule>()
            .HasOne(r => r.ExamVariant)
            .WithMany(v => v.ScoringRules)
            .HasForeignKey(r => r.ExamVariantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamScoringRule>()
            .HasOne(r => r.ExamSection)
            .WithMany(s => s.ScoringRules)
            .HasForeignKey(r => r.ExamSectionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamScoringRule>()
            .Property(r => r.RuleType)
            .HasMaxLength(64);

        modelBuilder.Entity<ExamScoringRule>()
            .Property(r => r.Label)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamScoringRule>()
            .Property(r => r.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<ExamScoringRule>()
            .HasIndex(r => new { r.ExamVariantId, r.ExamSectionId, r.RuleType, r.IsDeleted });

        modelBuilder.Entity<ExamTimeRule>()
            .HasOne(r => r.ExamVariant)
            .WithMany(v => v.TimeRules)
            .HasForeignKey(r => r.ExamVariantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamTimeRule>()
            .HasOne(r => r.ExamSection)
            .WithMany(s => s.TimeRules)
            .HasForeignKey(r => r.ExamSectionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamTimeRule>()
            .Property(r => r.RuleType)
            .HasMaxLength(64);

        modelBuilder.Entity<ExamTimeRule>()
            .Property(r => r.Label)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamTimeRule>()
            .Property(r => r.MetadataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<ExamTimeRule>()
            .HasIndex(r => new { r.ExamVariantId, r.ExamSectionId, r.RuleType, r.IsDeleted });

        modelBuilder.Entity<ExamContentPack>()
            .HasOne(p => p.ExamDefinition)
            .WithMany(d => d.ContentPacks)
            .HasForeignKey(p => p.ExamDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamContentPack>()
            .HasOne(p => p.OwnerUser)
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamContentPack>()
            .HasOne(p => p.ImportedByUser)
            .WithMany()
            .HasForeignKey(p => p.ImportedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.Visibility)
            .HasMaxLength(32);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.SourceOrigin)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.LicenseStatus)
            .HasMaxLength(128);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.VerificationStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.SourceTitle)
            .HasMaxLength(512);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.SourceUrl)
            .HasMaxLength(1024);

        modelBuilder.Entity<ExamContentPack>()
            .Property(p => p.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<ExamContentPack>()
            .HasIndex(p => new { p.ExamDefinitionId, p.OwnerUserId, p.Code, p.IsDeleted });

        modelBuilder.Entity<ExamContentPack>()
            .HasIndex(p => new { p.ImportedByUserId, p.CreatedAt });

        modelBuilder.Entity<QuestionItem>()
            .HasOne(q => q.OwnerUser)
            .WithMany()
            .HasForeignKey(q => q.OwnerUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionItem>()
            .HasOne(q => q.ExamDefinition)
            .WithMany()
            .HasForeignKey(q => q.ExamDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionItem>()
            .HasOne(q => q.ExamVariant)
            .WithMany()
            .HasForeignKey(q => q.ExamVariantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionItem>()
            .HasOne(q => q.ExamSection)
            .WithMany()
            .HasForeignKey(q => q.ExamSectionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionItem>()
            .HasOne(q => q.ExamSubject)
            .WithMany()
            .HasForeignKey(q => q.ExamSubjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionItem>()
            .HasOne(q => q.ExamTopic)
            .WithMany()
            .HasForeignKey(q => q.ExamTopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionItem>()
            .HasOne(q => q.ExamOutcome)
            .WithMany()
            .HasForeignKey(q => q.ExamOutcomeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.QuestionType)
            .HasMaxLength(64);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.Difficulty)
            .HasMaxLength(64);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.CognitiveSkill)
            .HasMaxLength(128);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.QualityStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.LicenseStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.SourceOrigin)
            .HasMaxLength(128);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.SourceTitle)
            .HasMaxLength(512);

        modelBuilder.Entity<QuestionItem>()
            .Property(q => q.SourceUrl)
            .HasMaxLength(1024);

        modelBuilder.Entity<QuestionItem>()
            .HasIndex(q => new { q.OwnerUserId, q.ExamDefinitionId, q.QualityStatus, q.IsDeleted });

        modelBuilder.Entity<QuestionItem>()
            .HasIndex(q => new { q.ExamDefinitionId, q.ExamVariantId, q.ExamSectionId, q.ExamSubjectId, q.ExamTopicId, q.ExamOutcomeId });

        modelBuilder.Entity<QuestionItem>()
            .HasIndex(q => new { q.QuestionType, q.Difficulty, q.QualityStatus, q.IsDeleted });

        modelBuilder.Entity<QuestionOption>()
            .HasOne(o => o.QuestionItem)
            .WithMany(q => q.Options)
            .HasForeignKey(o => o.QuestionItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionOption>()
            .Property(o => o.OptionKey)
            .HasMaxLength(32);

        modelBuilder.Entity<QuestionOption>()
            .HasIndex(o => new { o.QuestionItemId, o.OptionKey });

        modelBuilder.Entity<QuestionExplanation>()
            .HasOne(e => e.QuestionItem)
            .WithMany(q => q.Explanations)
            .HasForeignKey(e => e.QuestionItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionExplanation>()
            .Property(e => e.SourceTitle)
            .HasMaxLength(512);

        modelBuilder.Entity<QuestionExplanation>()
            .Property(e => e.SourceUrl)
            .HasMaxLength(1024);

        modelBuilder.Entity<QuestionExplanation>()
            .Property(e => e.Visibility)
            .HasMaxLength(64);

        modelBuilder.Entity<QuestionExplanation>()
            .HasIndex(e => new { e.QuestionItemId, e.Visibility, e.IsDeleted });

        modelBuilder.Entity<QuestionTag>()
            .HasOne(t => t.QuestionItem)
            .WithMany(q => q.Tags)
            .HasForeignKey(t => t.QuestionItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionTag>()
            .Property(t => t.Tag)
            .HasMaxLength(128);

        modelBuilder.Entity<QuestionTag>()
            .HasIndex(t => new { t.QuestionItemId, t.Tag });

        modelBuilder.Entity<QuestionOutcomeLink>()
            .HasOne(l => l.QuestionItem)
            .WithMany(q => q.OutcomeLinks)
            .HasForeignKey(l => l.QuestionItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionOutcomeLink>()
            .HasOne(l => l.ExamOutcome)
            .WithMany()
            .HasForeignKey(l => l.ExamOutcomeId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionOutcomeLink>()
            .Property(l => l.LinkStrength)
            .HasPrecision(6, 4);

        modelBuilder.Entity<QuestionOutcomeLink>()
            .HasIndex(l => new { l.QuestionItemId, l.ExamOutcomeId, l.IsDeleted });

        modelBuilder.Entity<QuestionOutcomeLink>()
            .HasIndex(l => new { l.ExamOutcomeId, l.IsPrimary, l.IsDeleted });

        modelBuilder.Entity<QuestionImportPreview>()
            .HasOne(p => p.OwnerUser)
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<QuestionImportPreview>()
            .Property(p => p.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<QuestionImportPreview>()
            .HasIndex(p => new { p.OwnerUserId, p.Status, p.ExpiresAt, p.IsDeleted });

        modelBuilder.Entity<QuestionImportPreviewItem>()
            .HasOne(i => i.Preview)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.QuestionImportPreviewId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionImportPreviewItem>()
            .Property(i => i.ExternalId)
            .HasMaxLength(128);

        modelBuilder.Entity<QuestionImportPreviewItem>()
            .Property(i => i.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<QuestionImportPreviewItem>()
            .Property(i => i.IssuesJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<QuestionImportPreviewItem>()
            .Property(i => i.NormalizedQuestionJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<QuestionImportPreviewItem>()
            .HasIndex(i => new { i.QuestionImportPreviewId, i.Status, i.IsDeleted });

        modelBuilder.Entity<QuestionImportPreviewItem>()
            .HasIndex(i => new { i.QuestionImportPreviewId, i.ExternalId });

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasOne(a => a.ExamDefinition)
            .WithMany()
            .HasForeignKey(a => a.ExamDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasOne(a => a.ExamVariant)
            .WithMany()
            .HasForeignKey(a => a.ExamVariantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasOne(a => a.ExamSection)
            .WithMany()
            .HasForeignKey(a => a.ExamSectionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasOne(a => a.ExamSubject)
            .WithMany()
            .HasForeignKey(a => a.ExamSubjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasOne(a => a.ExamTopic)
            .WithMany()
            .HasForeignKey(a => a.ExamTopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .Property(a => a.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .Property(a => a.ExamCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .Property(a => a.VariantCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .Property(a => a.SectionCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .Property(a => a.SubjectCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .Property(a => a.TopicCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasIndex(a => new { a.UserId, a.Status, a.StartedAt, a.IsDeleted });

        modelBuilder.Entity<CentralExamPracticeAttempt>()
            .HasIndex(a => new { a.UserId, a.ExamDefinitionId, a.ExamTopicId, a.StartedAt });

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .HasOne(a => a.PracticeAttempt)
            .WithMany(a => a.Answers)
            .HasForeignKey(a => a.PracticeAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .HasOne(a => a.QuestionItem)
            .WithMany()
            .HasForeignKey(a => a.QuestionItemId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .HasOne(a => a.ExamOutcome)
            .WithMany()
            .HasForeignKey(a => a.ExamOutcomeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .HasOne(a => a.ExamTopic)
            .WithMany()
            .HasForeignKey(a => a.ExamTopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.SelectedOptionKey)
            .HasMaxLength(32);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.CorrectOptionKey)
            .HasMaxLength(32);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.OptionKeysJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.TopicCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.OutcomeCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.QuestionType)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.Difficulty)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.SourceTitle)
            .HasMaxLength(512);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .Property(a => a.SourceUrl)
            .HasMaxLength(1024);

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .HasIndex(a => new { a.PracticeAttemptId, a.QuestionItemId });

        modelBuilder.Entity<CentralExamPracticeAnswer>()
            .HasIndex(a => new { a.ExamTopicId, a.ExamOutcomeId, a.IsCorrect, a.IsBlank });

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .HasOne(b => b.ExamDefinition)
            .WithMany()
            .HasForeignKey(b => b.ExamDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .HasOne(b => b.ExamVariant)
            .WithMany()
            .HasForeignKey(b => b.ExamVariantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .HasOne(b => b.OwnerUser)
            .WithMany()
            .HasForeignKey(b => b.OwnerUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .Property(b => b.Code)
            .HasMaxLength(128);

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .Property(b => b.Name)
            .HasMaxLength(256);

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .Property(b => b.Visibility)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .Property(b => b.VerificationStatus)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeBlueprint>()
            .HasIndex(b => new { b.Code, b.OwnerUserId, b.IsDeleted });

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .HasOne(s => s.Blueprint)
            .WithMany(b => b.Sections)
            .HasForeignKey(s => s.BlueprintId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .HasOne(s => s.ExamSection)
            .WithMany()
            .HasForeignKey(s => s.ExamSectionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .HasOne(s => s.ExamSubject)
            .WithMany()
            .HasForeignKey(s => s.ExamSubjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .HasOne(s => s.ExamTopic)
            .WithMany()
            .HasForeignKey(s => s.ExamTopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .Property(s => s.SectionCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .Property(s => s.SubjectCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .Property(s => s.TopicCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .Property(s => s.DifficultyMixJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<CentralExamDenemeBlueprintSection>()
            .HasIndex(s => new { s.BlueprintId, s.SortOrder, s.IsDeleted });

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .HasOne(a => a.Blueprint)
            .WithMany()
            .HasForeignKey(a => a.BlueprintId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .HasOne(a => a.ExamDefinition)
            .WithMany()
            .HasForeignKey(a => a.ExamDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .HasOne(a => a.ExamVariant)
            .WithMany()
            .HasForeignKey(a => a.ExamVariantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .Property(a => a.ExamCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .Property(a => a.VariantCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .Property(a => a.Status)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .HasIndex(a => new { a.UserId, a.Status, a.StartedAt, a.IsDeleted });

        modelBuilder.Entity<CentralExamDenemeAttempt>()
            .HasIndex(a => new { a.UserId, a.BlueprintId, a.StartedAt });

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasOne(a => a.DenemeAttempt)
            .WithMany(a => a.Answers)
            .HasForeignKey(a => a.DenemeAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasOne(a => a.QuestionItem)
            .WithMany()
            .HasForeignKey(a => a.QuestionItemId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasOne(a => a.ExamSection)
            .WithMany()
            .HasForeignKey(a => a.ExamSectionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasOne(a => a.ExamSubject)
            .WithMany()
            .HasForeignKey(a => a.ExamSubjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasOne(a => a.ExamTopic)
            .WithMany()
            .HasForeignKey(a => a.ExamTopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasOne(a => a.ExamOutcome)
            .WithMany()
            .HasForeignKey(a => a.ExamOutcomeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.SelectedOptionKey)
            .HasMaxLength(32);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.CorrectOptionKey)
            .HasMaxLength(32);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.OptionKeysJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.SectionCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.SubjectCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.TopicCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.OutcomeCode)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.QuestionType)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.Difficulty)
            .HasMaxLength(64);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.SourceTitle)
            .HasMaxLength(512);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .Property(a => a.SourceUrl)
            .HasMaxLength(1024);

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasIndex(a => new { a.DenemeAttemptId, a.QuestionItemId });

        modelBuilder.Entity<CentralExamDenemeAnswer>()
            .HasIndex(a => new { a.ExamSectionId, a.ExamSubjectId, a.ExamTopicId, a.ExamOutcomeId });
    }
}
