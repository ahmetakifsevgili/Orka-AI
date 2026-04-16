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
    public DbSet<AgentEvaluation> AgentEvaluations { get; set; } = null!;
    public DbSet<SkillMastery> SkillMasteries { get; set; } = null!;

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

        // Session: kullanıcı + konu bazlı oturum arama
        modelBuilder.Entity<Session>()
            .HasIndex(s => new { s.UserId, s.TopicId });

        // Message: oturum mesaj listesi + sıralama
        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.SessionId, m.CreatedAt });

        // WikiPage: konu bazlı wiki içerik yükleme
        modelBuilder.Entity<WikiPage>()
            .HasIndex(w => w.TopicId);
    }
}
