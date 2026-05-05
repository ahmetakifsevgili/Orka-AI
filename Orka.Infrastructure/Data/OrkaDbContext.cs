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
            .HasIndex(rt => rt.Token)
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
            .HasIndex(qa => new { qa.UserId, qa.TopicId, qa.SkillTag });

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
    }
}
