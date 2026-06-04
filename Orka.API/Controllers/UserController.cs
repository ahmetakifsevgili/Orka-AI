using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Linq;

namespace Orka.API.Controllers;

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}
public class UpdateSettingsRequest
{
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public string? FontSize { get; set; }
    public bool? QuizReminders { get; set; }
    public bool? WeeklyReport { get; set; }
    public bool? NewContentAlerts { get; set; }
    public bool? SoundsEnabled { get; set; }
}

[Authorize]
[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IDataLifecycleService _dataLifecycle;

    public UserController(OrkaDbContext dbContext, IConfiguration configuration, IDataLifecycleService dataLifecycle)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _dataLifecycle = dataLifecycle;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        var dailyLimit = user.Plan == UserPlan.Pro
            ? _configuration.GetValue<int>("Limits:ProUserDailyMessages", 500)
            : _configuration.GetValue<int>("Limits:FreeUserDailyMessages", 50);

        return Ok(new
        {
            id = user.Id,
            firstName = user.FirstName,
            lastName = user.LastName,
            email = user.Email,
            plan = user.Plan.ToString(),
            isAdmin = user.IsAdmin,
            storageUsedMB = user.StorageUsedMB,
            storageLimitMB = user.StorageLimitMB,
            dailyMessageCount = user.DailyMessageCount,
            dailyLimit,
            dailyResetAt = user.DailyMessageResetAt,
            createdAt = user.CreatedAt,
            settings = new 
            {
                theme = user.Theme,
                language = user.Language,
                fontSize = user.FontSize,
                quizReminders = user.QuizReminders,
                weeklyReport = user.WeeklyReport,
                newContentAlerts = user.NewContentAlerts,
                soundsEnabled = user.SoundsEnabled
            }
        });
    }

    [HttpPatch("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.FirstName)) user.FirstName = req.FirstName;
        if (!string.IsNullOrWhiteSpace(req.LastName)) user.LastName = req.LastName;
        
        // E-mail değişikliği dikkat gerektirir ancak basit tutuyoruz:
        if (!string.IsNullOrWhiteSpace(req.Email)) 
        {
            var exists = await _dbContext.Users.AnyAsync(u => u.Email == req.Email && u.Id != userId);
            if (exists) return BadRequest("Bu e-posta zaten kullanımda.");
            user.Email = req.Email;
        }

        await _dbContext.SaveChangesAsync();
        return Ok(new { Message = "Profil başarıyla güncellendi." });
    }

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req)
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        if (req.Theme != null) user.Theme = req.Theme;
        if (req.Language != null) user.Language = req.Language;
        if (req.FontSize != null) user.FontSize = req.FontSize;
        if (req.QuizReminders.HasValue) user.QuizReminders = req.QuizReminders.Value;
        if (req.WeeklyReport.HasValue) user.WeeklyReport = req.WeeklyReport.Value;
        if (req.NewContentAlerts.HasValue) user.NewContentAlerts = req.NewContentAlerts.Value;
        if (req.SoundsEnabled.HasValue) user.SoundsEnabled = req.SoundsEnabled.Value;

        await _dbContext.SaveChangesAsync();
        return Ok(new { Message = "Ayarlar başarıyla kaydedildi." });
    }

    /// <summary>
    /// Kullanıcının gamification istatistiklerini döner: TotalXP, Streak, Level bilgisi.
    /// </summary>
    [HttpGet("gamification")]
    public async Task<IActionResult> GetGamification()
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        // Level hesaplama: her 100 XP = 1 level
        var level     = (user.TotalXP / 100) + 1;
        var xpInLevel = user.TotalXP % 100;
        var xpToNext  = 100 - xpInLevel;
        var badges = await _dbContext.UserBadges
            .AsNoTracking()
            .Include(ub => ub.Badge)
            .Where(ub => ub.UserId == userId)
            .OrderByDescending(ub => ub.EarnedAt)
            .Select(ub => new
            {
                ub.Badge.Id,
                ub.Badge.Code,
                ub.Badge.Name,
                ub.Badge.Description,
                ub.Badge.IconKey,
                ub.Badge.RuleType,
                ub.Badge.Threshold,
                ub.EarnedAt
            })
            .ToListAsync();

        return Ok(new
        {
            totalXP        = user.TotalXP,
            currentStreak  = user.CurrentStreak,
            lastActiveDate = user.LastActiveDate,
            level,
            xpInLevel,
            xpToNextLevel  = xpToNext,
            levelLabel     = level switch
            {
                1 => "Beginner",
                2 => "Learner",
                3 => "Explorer",
                4 => "Scholar",
                5 => "Expert",
                _ => $"Master Lv.{level}"
            },
            badges
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportData()
    {
        var userId = GetUserId();
        var ct = HttpContext.RequestAborted;
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return NotFound();

        var topics = await _dbContext.Topics.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.Title, t.CreatedAt })
            .ToListAsync(ct);

        var sessions = await _dbContext.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Id, s.TopicId, s.CurrentState, s.CreatedAt })
            .ToListAsync(ct);

        var messages = await _dbContext.Messages.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Id, m.SessionId, m.Content, m.MessageType, m.CreatedAt })
            .ToListAsync(ct);

        var sources = await _dbContext.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Id, s.TopicId, s.Title, s.SourceType, s.CreatedAt })
            .ToListAsync(ct);

        var sourceIds = sources.Select(s => s.Id).ToList();
        var sourceChunks = await _dbContext.SourceChunks.AsNoTracking()
            .Where(c => sourceIds.Contains(c.LearningSourceId) && !c.IsDeleted)
            .Select(c => new { c.Id, c.LearningSourceId, c.PageNumber, c.ChunkIndex, c.Text, c.HighlightHint, HasEmbedding = c.EmbeddingJson != null, c.CreatedAt })
            .ToListAsync(ct);

        var wikiPages = await _dbContext.WikiPages.AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => new { w.Id, w.TopicId, w.SessionId, w.PageKey, w.PageType, w.Title, w.Content, w.SourceReadiness, w.EvidenceStatus, w.SafeSummary, w.Status, w.CreatedAt, w.UpdatedAt })
            .ToListAsync(ct);

        var wikiPageIds = wikiPages.Select(w => w.Id).ToList();
        var wikiBlocks = await _dbContext.WikiBlocks.AsNoTracking()
            .Where(b => wikiPageIds.Contains(b.WikiPageId))
            .Select(b => new { b.Id, b.WikiPageId, b.BlockType, b.Title, b.Content, b.SourceBasis, b.ConceptKey, b.MisconceptionKey, b.QuizAttemptId, b.SourceEvidenceBundleId, b.Visibility, b.SafetyWarningsJson, b.CreatedAt, b.UpdatedAt })
            .ToListAsync(ct);

        var quizAttempts = await _dbContext.QuizAttempts.AsNoTracking()
            .Where(q => q.UserId == userId)
            .Select(q => new
            {
                q.Id,
                q.QuizRunId,
                q.SessionId,
                q.TopicId,
                q.Question,
                q.UserAnswer,
                q.IsCorrect,
                q.Explanation,
                q.SkillTag,
                q.AssessmentItemId,
                q.TopicPath,
                q.Difficulty,
                q.CognitiveType,
                q.QuestionHash,
                q.SourceRefsJson,
                q.ResponseTimeMs,
                q.WasSkipped,
                q.ConfidenceSelfRating,
                q.CreatedAt
            })
            .ToListAsync(ct);

        var learningSignals = await _dbContext.LearningSignals.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Id, s.TopicId, s.SessionId, s.QuizAttemptId, s.SignalType, s.SkillTag, s.TopicPath, s.Score, s.IsPositive, s.PayloadJson, s.CreatedAt })
            .ToListAsync(ct);

        var sourceEvidenceBundles = await _dbContext.SourceEvidenceBundles.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDeleted)
            .Select(b => new
            {
                b.Id,
                b.TopicId,
                b.SessionId,
                b.BundleHash,
                b.EvidenceStatus,
                b.SourceCount,
                b.ReadySourceCount,
                b.ChunkCount,
                b.CitationCoverage,
                b.UnsupportedCitationCount,
                b.StaleEvidenceCount,
                b.DeletedEvidenceCount,
                b.EvidenceJson,
                b.CreatedAt,
                b.UpdatedAt,
                b.ExpiresAt
            })
            .ToListAsync(ct);

        var tutorMemory = await _dbContext.TutorMemoryFragments.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Id, m.TopicId, m.SessionId, m.TutorTurnStateId, m.TutorActionTraceId, m.Content, m.CreatedAt })
            .ToListAsync(ct);

        var tutorWorkingMemory = await _dbContext.TutorWorkingMemorySnapshots.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Id, m.TopicId, m.SessionId, m.ActiveConceptKey, m.TeachingMode, m.StyleMode, m.AffectiveState, m.CognitiveLoad, m.Source, m.IsDegraded, m.SnapshotJson, m.CreatedAt, m.ExpiresAt })
            .ToListAsync(ct);

        var tutorTurnStates = await _dbContext.TutorTurnStates.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Id, m.TopicId, m.SessionId, m.WorkingMemorySnapshotId, m.ActiveConceptKey, m.TeachingMode, m.StyleMode, m.AffectiveState, m.CognitiveLoad, m.GroundingStatus, m.StateJson, m.CreatedAt })
            .ToListAsync(ct);

        var providerEvidence = await _dbContext.TeachingEvidenceItems.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Id, e.TopicId, e.SessionId, e.EvidenceType, e.Provider, e.Query, e.Title, e.RawPayloadJson, e.CreatedAt })
            .ToListAsync(ct);

        var reviewItems = await _dbContext.ReviewItems.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new
            {
                r.Id,
                r.TopicId,
                r.ReviewKey,
                r.SkillTag,
                r.ConceptTag,
                r.LearningObjective,
                r.MistakeCategory,
                r.SourceType,
                r.SourceId,
                r.DueAt,
                r.LastReviewedAt,
                r.IntervalDays,
                r.EaseFactor,
                r.RepetitionCount,
                r.LapseCount,
                r.SuccessStreak,
                r.Status,
                r.QuizAttemptId,
                r.LearningSignalId,
                r.FlashcardId,
                r.RemediationPlanId,
                r.MetadataJson,
                r.CreatedAt,
                r.UpdatedAt
            })
            .ToListAsync(ct);

        var flashcards = await _dbContext.Flashcards.AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => new
            {
                f.Id,
                f.TopicId,
                f.LearningSourceId,
                f.WikiPageId,
                f.QuizAttemptId,
                f.Front,
                f.Back,
                f.Hint,
                f.SkillTag,
                f.ConceptTag,
                f.LearningObjective,
                f.Difficulty,
                f.Status,
                f.CreatedFrom,
                f.MetadataJson,
                f.CreatedAt,
                f.UpdatedAt,
                f.LastReviewedAt
            })
            .ToListAsync(ct);

        var learningArtifacts = await _dbContext.LearningArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .Select(a => new
            {
                a.Id,
                a.TopicId,
                a.SessionId,
                a.SourceEvidenceBundleId,
                a.WikiNotebookSectionKey,
                a.ConceptKey,
                a.ConceptLabel,
                a.ArtifactType,
                a.ArtifactStatus,
                a.Origin,
                a.RenderFormat,
                a.Title,
                a.SafeContent,
                a.ContentJson,
                a.SourceBasis,
                a.CitationIdsJson,
                a.SafetyWarningsJson,
                a.CreatedAt,
                a.UpdatedAt,
                a.ExpiresAt
            })
            .ToListAsync(ct);

        var teachingArtifacts = await _dbContext.TeachingArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new
            {
                a.Id,
                a.TopicId,
                a.SessionId,
                a.TutorActionTraceId,
                a.ArtifactType,
                a.Title,
                a.Content,
                a.RenderFormat,
                a.Status,
                a.Provider,
                a.ExternalUrl,
                a.RenderError,
                a.MetadataJson,
                a.RenderedAt,
                a.CreatedAt
            })
            .ToListAsync(ct);

        var tutorMemoryPatches = await _dbContext.TutorMemoryPatches.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Id, p.TopicId, p.SessionId, p.PatchType, p.PatchJson, p.Source, p.CreatedAt })
            .ToListAsync(ct);

        var audioOverviewJobs = await _dbContext.AudioOverviewJobs.AsNoTracking()
            .Where(j => j.UserId == userId)
            .Select(j => new
            {
                j.Id,
                j.TopicId,
                j.SessionId,
                j.Status,
                j.Script,
                j.SpeakersJson,
                j.ContentType,
                j.AudioByteLength,
                j.AudioExpiresAt,
                j.AudioPurgedAt,
                j.ErrorMessage,
                j.CreatedAt,
                j.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(new
        {
            profile = new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.CreatedAt
            },
            topics,
            sessions,
            messages,
            sources,
            sourceChunks,
            wikiPages,
            wikiBlocks,
            learningSignals,
            tutorMemory,
            tutorMemoryPatches,
            tutorWorkingMemory,
            tutorTurnStates,
            providerEvidence,
            quizAttempts,
            reviewItems,
            flashcards,
            learningArtifacts,
            teachingArtifacts,
            audioOverviewJobs,
            sourceEvidenceBundles,
            retentionPolicy = new
            {
                chat = "exported_and_deleted_on_account_or_topic_delete",
                wiki = "exported_and_deleted_on_account_or_topic_delete",
                sources = "exported_and_deleted_on_account_or_topic_delete",
                embeddings = "raw_vectors_not_exported_deleted_with_source_chunks",
                tutorMemory = "exported_and_deleted_on_account_or_topic_delete",
                providerEvidence = "exported_and_deleted_on_account_or_topic_delete",
                audioBytes = "time_limited_retention_policy"
            }
        });
    }

    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = GetUserId();
        var deleted = await _dataLifecycle.DeleteAccountAsync(userId, HttpContext.RequestAborted);
        if (!deleted) return NotFound();

        return Ok(new { Message = "Hesap ve tüm ilişkili veriler kalıcı olarak silindi." });
    }
}
