using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Core.DTOs;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class WikiService : IWikiService
{
    // [HATA-6 DÜZELTMESİ]: IServiceScopeFactory — her metot kendi scope'unu açar (thread-safe)
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGroqService _groq;
    private readonly ITopicScopeResolver _topicScopeResolver;
    private readonly ILogger<WikiService> _logger;

    public WikiService(IServiceScopeFactory scopeFactory,
        IGroqService groq,
        ITopicScopeResolver topicScopeResolver,
        ILogger<WikiService> logger)
    {
        _scopeFactory = scopeFactory;
        _groq = groq;
        _topicScopeResolver = topicScopeResolver;
        _logger = logger;
    }

    public async Task<IEnumerable<WikiPage>> GetTopicWikiPagesAsync(Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        {
            var topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId);
            if (!topicScope.IsValid) return [];

            var readTopicIds = GetWikiReadTopicIds(topicScope);
            var topicOrder = readTopicIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index);

            var scopedPages = await db.WikiPages
                .Include(w => w.Blocks)
                .Where(w => readTopicIds.Contains(w.TopicId) && w.UserId == userId && !w.IsDeleted)
                .ToListAsync();

            return scopedPages
                .DistinctBy(w => w.Id)
                .OrderBy(w => topicOrder.GetValueOrDefault(w.TopicId, int.MaxValue))
                .ThenBy(w => w.OrderIndex)
                .ThenBy(w => w.CreatedAt)
                .ToList();
        }

        // Önce kullanıcının kendi sayfalarını bul

        // Eğer parent topic ise, subtopic'lerin wiki sayfalarını da getir

        // Fallback: Topic sahibini kontrol et
    }

    public async Task<WikiPage?> GetWikiPageAsync(Guid pageId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.WikiPages
            .Include(w => w.Blocks)
            .Include(w => w.Sources)
            .FirstOrDefaultAsync(w => w.Id == pageId && w.UserId == userId && !w.IsDeleted);
    }

    public async Task<WikiBlock> AddUserNoteAsync(Guid pageId, Guid userId, string content)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var page = await db.WikiPages.FirstOrDefaultAsync(w => w.Id == pageId && w.UserId == userId && !w.IsDeleted)
            ?? throw new Exception("Wiki sayfası bulunamadı.");

        var maxOrder = await db.WikiBlocks
            .Where(b => b.WikiPageId == pageId && !b.IsDeleted)
            .MaxAsync(b => (int?)b.OrderIndex) ?? 0;

        var block = new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = pageId,
            BlockType = WikiBlockType.ManualNote,
            Title = "Notum",
            Content = SanitizePublicText(content, 4000, out var warnings),
            Source = "user",
            SourceBasis = "model_assisted",
            Visibility = "normal",
            SafetyWarningsJson = JsonSerializer.Serialize(warnings),
            OrderIndex = maxOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.WikiBlocks.Add(block);
        await db.SaveChangesAsync();
        return block;
    }

    public async Task<WikiBlockDto?> AddWikiBlockAsync(Guid pageId, Guid userId, CreateWikiBlockRequestDto request)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var page = await db.WikiPages.FirstOrDefaultAsync(w => w.Id == pageId && w.UserId == userId && !w.IsDeleted);
        if (page == null) return null;

        var content = SanitizePublicText(request.Content, 8000, out var contentWarnings);
        if (string.IsNullOrWhiteSpace(content)) return null;

        var title = SanitizePublicText(request.Title, 240, out var titleWarnings);
        var source = SanitizePublicText(request.Source, 160, out var sourceWarnings);
        var warnings = contentWarnings
            .Concat(titleWarnings)
            .Concat(sourceWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sourceBasis = NormalizeSourceBasis(request.SourceBasis);
        var sourceEvidenceBundleId = request.SourceEvidenceBundleId;

        if (sourceBasis == "source_grounded")
        {
            var hasReadyBundle = sourceEvidenceBundleId.HasValue && await db.SourceEvidenceBundles.AnyAsync(b =>
                b.Id == sourceEvidenceBundleId.Value &&
                b.UserId == userId &&
                b.TopicId == page.TopicId &&
                b.EvidenceStatus == "source_grounded");
            if (!hasReadyBundle)
            {
                sourceBasis = "evidence_insufficient";
                sourceEvidenceBundleId = null;
                warnings.Add("source_grounded_bundle_missing_or_not_ready");
            }
        }

        var now = DateTime.UtcNow;
        var maxOrder = await db.WikiBlocks
            .Where(b => b.WikiPageId == pageId && !b.IsDeleted)
            .MaxAsync(b => (int?)b.OrderIndex) ?? 0;
        var block = new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = pageId,
            BlockType = NormalizeWikiBlockType(request.BlockType),
            Title = string.IsNullOrWhiteSpace(title) ? BuildDefaultBlockTitle(request.BlockType) : title!,
            Content = content,
            Source = source,
            SourceBasis = sourceBasis,
            ConceptKey = CleanText(request.ConceptKey, 180) ?? page.ConceptKey,
            MisconceptionKey = CleanText(request.MisconceptionKey, 180),
            QuizAttemptId = request.QuizAttemptId,
            SourceEvidenceBundleId = sourceEvidenceBundleId,
            LearningArtifactId = request.LearningArtifactId,
            TutorTurnStateId = request.TutorTurnStateId,
            Visibility = NormalizeVisibility(request.Visibility),
            SafetyWarningsJson = JsonSerializer.Serialize(warnings),
            OrderIndex = maxOrder + 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.WikiBlocks.Add(block);
        if (string.IsNullOrWhiteSpace(page.SafeSummary))
        {
            page.SafeSummary = CleanText(content, 1200);
        }
        if (page.Status is "pending" or "learning")
        {
            page.Status = "ready";
        }
        page.UpdatedAt = now;
        await db.SaveChangesAsync();

        return ToBlockDto(block);
    }

    public async Task UpdateWikiBlockAsync(Guid blockId, Guid userId, string? title, string? content)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var block = await db.WikiBlocks
            .Include(b => b.WikiPage)
            .FirstOrDefaultAsync(b => b.Id == blockId && b.WikiPage.UserId == userId && !b.IsDeleted && !b.WikiPage.IsDeleted);

        if (block == null) return;

        if (title != null) block.Title = SanitizePublicText(title, 240, out _);
        if (content != null)
        {
            block.Content = SanitizePublicText(content, 8000, out var warnings);
            if (warnings.Count > 0) block.SafetyWarningsJson = JsonSerializer.Serialize(warnings);
        }
        block.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task DeleteWikiBlockAsync(Guid blockId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var block = await db.WikiBlocks
            .Include(b => b.WikiPage)
            .FirstOrDefaultAsync(b => b.Id == blockId && b.WikiPage.UserId == userId && !b.IsDeleted && !b.WikiPage.IsDeleted);

        if (block == null) return;

        if (block.BlockType is not (WikiBlockType.UserNote or WikiBlockType.ManualNote or WikiBlockType.StudentQuestion))
            throw new Exception("Sadece kullanıcı notları silinebilir.");

        block.IsDeleted = true;
        block.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task AutoUpdateWikiAsync(Guid topicId, string aiContent, string userQuestion, string modelUsed)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var topic = await db.Topics.FindAsync(topicId);
        if (topic == null) return;

        // ── Subtopic desteği: Eğer parent topic ise, aktif subtopic'i bul ─────
        Guid wikiTopicId = topicId;
        string wikiTitle = topic.Title;
        
        var subTopics = await db.Topics
            .Where(t => t.ParentTopicId == topicId)
            .OrderBy(t => t.Order)
            .ToListAsync();
        
        if (subTopics.Count > 0)
        {
            // CompletedSections ile aktif subtopic'i bul
            var activeIndex = Math.Min(topic.CompletedSections, subTopics.Count - 1);
            var activeSub = subTopics[activeIndex];
            wikiTopicId = activeSub.Id;
            wikiTitle = activeSub.Title;
        }

        // ── Mevcut wiki sayfasını bul veya oluştur ───────────────────────────
        var page = await db.WikiPages
            .Where(w => w.TopicId == wikiTopicId && w.UserId == topic.UserId && !w.IsDeleted)
            .OrderBy(w => w.OrderIndex)
            .FirstOrDefaultAsync();

        if (page == null)
        {
            var resolvedUserId = topic.UserId;
            
            _logger.LogDebug("[WIKI] Yeni wiki sayfasi olusturuluyor. SubtopicRef={SubtopicRef} TitleRef={TitleRef}",
                LogPrivacyGuard.SafeId(wikiTopicId, "topic"),
                LogPrivacyGuard.SafeTextRef(wikiTitle, "title"));

            page = new WikiPage
            {
                Id = Guid.NewGuid(),
                TopicId = wikiTopicId,
                UserId = resolvedUserId,
                Title = wikiTitle,
                PageKey = BuildTopicPageKey(wikiTopicId),
                PageType = wikiTopicId == topicId ? "topic_root" : "lesson",
                SafeSummary = CleanText(aiContent, 1200),
                SourceReadiness = "evidence_insufficient",
                EvidenceStatus = "evidence_insufficient",
                MetadataJson = "{}",
                Status = "learning",
                OrderIndex = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.WikiPages.Add(page);
        }

        if (page.Status is "pending" or "ready")
        {
            page.Status = "learning";
            page.UpdatedAt = DateTime.UtcNow;
        }

        // ── Mevcut block sayısını kontrol et — her 3 etkileşimde bir özet üret ──
        var existingBlockCount = await db.WikiBlocks
            .Where(b => b.WikiPageId == page.Id && b.BlockType == WikiBlockType.Concept && !b.IsDeleted)
            .CountAsync();
        
        var maxOrder = await db.WikiBlocks
            .Where(b => b.WikiPageId == page.Id && !b.IsDeleted)
            .MaxAsync(b => (int?)b.OrderIndex) ?? 0;

        // ── Groq ile konuya özel özet üret (ham yanıt yerine) ─────────────────
        string summary;
        try
        {
            var summaryPrompt = $"""
                Aşağıdaki ders içeriğini, "{wikiTitle}" konusu için kısa ve öz bir wiki notu olarak yeniden yaz.
                
                KURALLAR:
                - Maksimum 3-4 paragraf
                - Sadece öğretici bilgi, gereksiz sohbet kısmını çıkar
                - Markdown formatı kullan (başlık, liste, kalın metin)
                - Kod varsa koru ama gereksiz açıklamaları kısalt
                - Türkçe yaz, sade bir dil kullan
                
                İçerik:
                {aiContent}
                """;
            
            summary = await _groq.GenerateResponseAsync(
                "Sen bir eğitim içerik editörüsün. Ders içeriklerini kısa, öz wiki notlarına dönüştürürsün.",
                summaryPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[WIKI] Groq ozet uretimi basarisiz, ham icerik kaydediliyor. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            // Fallback: ham içeriğin ilk 1000 karakterini kaydet
            summary = aiContent.Length > 1000 ? aiContent[..1000] + "\n\n..." : aiContent;
        }

        summary = PreserveDocCitationTags(summary, aiContent);

        var block = new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = page.Id,
            BlockType = WikiBlockType.Concept,
            Title = TruncateTitle(userQuestion, 60),
            Content = EnsureMarkdownIntegrity(summary),
            Source = modelUsed,
            SourceBasis = "model_assisted",
            ConceptKey = page.ConceptKey,
            Visibility = "normal",
            SafetyWarningsJson = "[]",
            OrderIndex = maxOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.WikiBlocks.Add(block);
        page.Status = "ready";
        page.Content = EnsureMarkdownIntegrity(summary);
        page.SafeSummary = CleanText(summary, 1200);
        page.UpdatedAt = DateTime.UtcNow;

        // QuizBlock: Her 3. wiki güncellemesinde pekiştirme soruları üret (her mesajda değil)
        if ((existingBlockCount + 1) % 3 == 0)
        {
            try
            {
                var prompt = $@"Aşağıdaki ders içeriğine dayanarak 3-5 adet pekiştirme sorusu hazırla.
Sorular kısa ve net cevaplı olmalı. Sadece soruları maddeler halinde dön.

Ders İçeriği:
{summary}";

                var questions = await _groq.GenerateResponseAsync(
                    "Sen bir eğitim küratörüsün. Sadece pekiştirme soruları hazırlarsın.",
                    prompt);

                var quizBlock = new WikiBlock
                {
                    Id = Guid.NewGuid(),
                    WikiPageId = page.Id,
                    BlockType = WikiBlockType.Quiz,
                    Title = "💡 Pekiştirme Soruları",
                    Content = questions,
                    Source = "groq-curator",
                    SourceBasis = "model_assisted",
                    ConceptKey = page.ConceptKey,
                    Visibility = "normal",
                    SafetyWarningsJson = "[]",
                    OrderIndex = maxOrder + 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.WikiBlocks.Add(quizBlock);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[WIKI CURATOR] Quiz uretimi basarisiz; ders akisi engellenmedi. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<string> GetWikiFullContentAsync(Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId);
        if (!topicScope.IsValid) return string.Empty;

        var readTopicIds = GetWikiReadTopicIds(topicScope);
        var topicOrder = readTopicIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);
        var pages = await db.WikiPages
            .Include(p => p.Blocks)
            .Where(p => readTopicIds.Contains(p.TopicId) && p.UserId == userId && !p.IsDeleted)
            .ToListAsync();

        pages = pages
            .DistinctBy(p => p.Id)
            .OrderBy(p => topicOrder.GetValueOrDefault(p.TopicId, int.MaxValue))
            .ThenBy(p => p.OrderIndex)
            .ThenBy(p => p.CreatedAt)
            .ToList();

        if (!pages.Any()) return string.Empty;

        var fullText = new System.Text.StringBuilder();
        foreach (var page in pages)
        {
            fullText.AppendLine($"# {page.Title}");
            foreach (var block in page.Blocks.Where(b => !b.IsDeleted).OrderBy(b => b.OrderIndex))
            {
                if (!string.IsNullOrEmpty(block.Title)) fullText.AppendLine($"## {block.Title}");
                fullText.AppendLine(block.Content);
                fullText.AppendLine();
            }
        }

        return fullText.ToString();
    }

    public async Task<WikiGraphDto> GetWikiGraphAsync(Guid topicId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId);
        if (!topicScope.IsValid)
        {
            return new WikiGraphDto
            {
                TopicId = topicId,
                GraphStatus = "not_found",
                Warnings = ["Topic bulunamadi veya kullanici kapsaminda degil."]
            };
        }

        var readTopicIds = GetWikiReadTopicIds(topicScope);
        var pages = await db.WikiPages
            .AsNoTracking()
            .Include(p => p.Blocks)
            .Where(p => p.UserId == userId && readTopicIds.Contains(p.TopicId) && !p.IsDeleted)
            .OrderBy(p => p.OrderIndex)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync();
        var pageIds = pages.Select(p => p.Id).ToHashSet();
        var links = await db.WikiLinks
            .AsNoTracking()
            .Where(l => l.UserId == userId &&
                        !l.IsDeleted &&
                        pageIds.Contains(l.SourcePageId) &&
                        (!l.TargetPageId.HasValue || pageIds.Contains(l.TargetPageId.Value)))
            .OrderBy(l => l.LinkType)
            .ThenByDescending(l => l.Strength)
            .ToListAsync();

        return BuildGraphDto(topicId, null, pages, links);
    }

    public async Task<WikiGraphDto?> GetLocalWikiGraphAsync(Guid pageId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var focus = await db.WikiPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pageId && p.UserId == userId && !p.IsDeleted);
        if (focus == null) return null;

        var links = await db.WikiLinks
            .AsNoTracking()
            .Where(l => l.UserId == userId &&
                        !l.IsDeleted &&
                        (l.SourcePageId == pageId || l.TargetPageId == pageId))
            .OrderBy(l => l.LinkType)
            .ThenByDescending(l => l.Strength)
            .ToListAsync();
        var relatedIds = links
            .SelectMany(l => new[] { l.SourcePageId }.Concat(l.TargetPageId.HasValue ? new[] { l.TargetPageId.Value } : Array.Empty<Guid>()))
            .Append(pageId)
            .Distinct()
            .ToArray();
        var pages = await db.WikiPages
            .AsNoTracking()
            .Include(p => p.Blocks)
            .Where(p => p.UserId == userId && relatedIds.Contains(p.Id) && !p.IsDeleted)
            .OrderBy(p => p.OrderIndex)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync();

        return BuildGraphDto(focus.TopicId, pageId, pages, links);
    }

    public async Task<WikiGraphLinkDto?> LinkWikiPagesAsync(Guid userId, CreateWikiLinkRequestDto request)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var source = await db.WikiPages
            .FirstOrDefaultAsync(p => p.Id == request.SourcePageId && p.UserId == userId && !p.IsDeleted);
        if (source == null) return null;

        WikiPage? target = null;
        if (request.TargetPageId.HasValue)
        {
            target = await db.WikiPages
                .FirstOrDefaultAsync(p => p.Id == request.TargetPageId.Value && p.UserId == userId && !p.IsDeleted);
            if (target == null) return null;
        }

        var linkType = NormalizeLinkType(request.LinkType);
        var targetKey = target?.PageKey ?? request.TargetPageKey?.Trim() ?? string.Empty;
        if (target == null && string.IsNullOrWhiteSpace(targetKey)) return null;

        var targetId = target?.Id;
        var existing = await db.WikiLinks
            .FirstOrDefaultAsync(l => l.UserId == userId &&
                                      l.SourcePageId == source.Id &&
                                      l.TargetPageId == targetId &&
                                      l.TargetPageKey == targetKey &&
                                      l.LinkType == linkType &&
                                      !l.IsDeleted);
        if (existing != null)
        {
            existing.Strength = ClampStrength(request.Strength);
            existing.SafeLabel = BuildSafeLinkLabel(request.SafeLabel, source.Title, target?.Title ?? targetKey, linkType);
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return ToLinkDto(existing);
        }

        var link = new WikiLink
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = source.TopicId,
            SourcePageId = source.Id,
            TargetPageId = target?.Id,
            TargetPageKey = targetKey,
            LinkType = linkType,
            Strength = ClampStrength(request.Strength),
            CreatedBy = NormalizeCreatedBy(request.CreatedBy),
            SafeLabel = BuildSafeLinkLabel(request.SafeLabel, source.Title, target?.Title ?? targetKey, linkType),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.WikiLinks.Add(link);
        await db.SaveChangesAsync();
        return ToLinkDto(link);
    }

    public async Task<WikiGraphSyncResultDto> SyncWikiGraphAsync(Guid topicId, Guid userId, WikiGraphSyncRequestDto request)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId);
        if (!topicScope.IsValid)
        {
            return new WikiGraphSyncResultDto
            {
                TopicId = topicId,
                SyncStatus = "not_found",
                Warnings = ["Konu bulunamadi veya kullanici kapsaminda degil."],
                Graph = new WikiGraphDto
                {
                    TopicId = topicId,
                    GraphStatus = "not_found",
                    Warnings = ["Konu bulunamadi veya kullanici kapsaminda degil."]
                }
            };
        }

        var warnings = new List<string>();
        var readTopicIds = GetWikiReadTopicIds(topicScope);
        var topics = await db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId && readTopicIds.Contains(t.Id))
            .OrderBy(t => t.Order)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
        var topicById = topics.ToDictionary(t => t.Id);
        var currentTopic = topicById.GetValueOrDefault(topicId);
        var graph = await ResolveConceptGraphForWikiAsync(db, userId, topicScope, request.ConceptGraphSnapshotId);
        if (graph == null && !request.IncludeTopicTreeFallback)
        {
            warnings.Add("Concept graph bulunamadi; fallback kapali oldugu icin Wiki sayfa iskeleti uretilmedi.");
        }

        var sourceBundle = await db.SourceEvidenceBundles
            .AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDeleted && b.TopicId.HasValue && readTopicIds.Contains(b.TopicId.Value))
            .OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();
        var evidenceStatus = NormalizeEvidenceStatus(sourceBundle?.EvidenceStatus);
        var sourceReadiness = evidenceStatus;
        if (sourceBundle == null)
        {
            warnings.Add("Source evidence bundle yok; Wiki sayfalari kaynakli kesinlik iddiasi tasimayacak.");
        }
        else if (sourceBundle.EvidenceStatus is "stale" or "degraded" or "evidence_insufficient")
        {
            warnings.Add("Kaynak durumu sinirli; Wiki sayfalari kaynak guvenini dusuk gosterecek.");
        }

        var createdPages = 0;
        var updatedPages = 0;
        var createdLinks = 0;
        var usedConceptGraph = false;
        var now = DateTime.UtcNow;

        WikiPage? rootPage = null;
        if (currentTopic != null)
        {
            (rootPage, var created, var updated) = await UpsertWikiPageAsync(
                db,
                userId,
                topicId,
                BuildTopicPageKey(topicId),
                "topic_root",
                null,
                null,
                null,
                graph?.Id,
                currentTopic.Title,
                $"Bu Wiki sayfasi {currentTopic.Title} konusu icin ana calisma girisidir.",
                sourceReadiness,
                evidenceStatus,
                0,
                now);
            if (created) createdPages++;
            if (updated) updatedPages++;
            if (request.CreateSummaryBlocks)
            {
                await EnsureSummaryBlockAsync(db, rootPage, "Ana sayfa", $"Bu sayfa {currentTopic.Title} icin ana not, soru, kaynak ve tekrar baglantilarini toplar.", "model_assisted", null, now);
            }
        }

        if (graph != null)
        {
            var concepts = await db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == graph.Id)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Label)
                .ToListAsync();
            var relations = await db.ConceptRelations
                .AsNoTracking()
                .Where(r => r.ConceptGraphSnapshotId == graph.Id)
                .ToListAsync();

            if (concepts.Count == 0)
            {
                warnings.Add("Concept graph var ama concept listesi bos; topic tree fallback kullanilacak.");
            }
            else
            {
                usedConceptGraph = true;
                var pageByConcept = new Dictionary<string, WikiPage>(StringComparer.OrdinalIgnoreCase);
                foreach (var concept in concepts)
                {
                    var description = string.IsNullOrWhiteSpace(concept.Description)
                        ? $"{concept.Label} icin calisma notlari ve kanit baglantilari burada toplanir."
                        : concept.Description.Trim();
                    (var page, var created, var updated) = await UpsertWikiPageAsync(
                        db,
                        userId,
                        topicId,
                        BuildConceptPageKey(concept.StableKey),
                        "concept",
                        concept.StableKey,
                        ResolveParentConceptKey(concept, relations),
                        rootPage?.Id,
                        graph.Id,
                        concept.Label,
                        description,
                        sourceReadiness,
                        evidenceStatus,
                        concept.Order + 1,
                        now);
                    pageByConcept[concept.StableKey] = page;
                    if (created) createdPages++;
                    if (updated) updatedPages++;
                    if (rootPage != null)
                    {
                        createdLinks += await EnsureWikiLinkAsync(db, userId, topicId, rootPage.Id, page.Id, page.PageKey, "parent_child", 1m, "system", $"{rootPage.Title} -> {page.Title}", now);
                    }

                    if (request.CreateSummaryBlocks)
                    {
                        await EnsureSummaryBlockAsync(db, page, concept.Label, description, "model_assisted", concept.StableKey, now);
                    }
                }

                foreach (var relation in relations)
                {
                    if (!pageByConcept.TryGetValue(relation.SourceConceptKey, out var source) ||
                        !pageByConcept.TryGetValue(relation.TargetConceptKey, out var target))
                    {
                        continue;
                    }

                    var linkType = NormalizeRelationLinkType(relation.RelationType);
                    createdLinks += await EnsureWikiLinkAsync(
                        db,
                        userId,
                        topicId,
                        source.Id,
                        target.Id,
                        target.PageKey,
                        linkType,
                        (decimal)Math.Clamp(relation.Weight, 0.1, 1.0),
                        "system",
                        $"{source.Title} -> {target.Title} ({linkType})",
                        now);
                }

                createdLinks += await EnsureSequenceLinksAsync(db, userId, topicId, concepts, pageByConcept, now);
            }
        }

        if (!usedConceptGraph && request.IncludeTopicTreeFallback)
        {
            if (graph == null)
            {
                warnings.Add("Concept graph bulunamadi; Wiki iskeleti topic tree fallback ile kuruldu.");
            }

            createdLinks += await SyncTopicTreePagesAsync(
                db,
                userId,
                topicId,
                topicScope,
                topics,
                rootPage,
                sourceReadiness,
                evidenceStatus,
                request.CreateSummaryBlocks,
                now,
                countPage: (created, updated) =>
                {
                    if (created) createdPages++;
                    if (updated) updatedPages++;
                });
        }

        await db.SaveChangesAsync();

        var pages = await db.WikiPages
            .AsNoTracking()
            .Include(p => p.Blocks)
            .Where(p => p.UserId == userId && readTopicIds.Contains(p.TopicId) && !p.IsDeleted)
            .OrderBy(p => p.OrderIndex)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync();
        var pageIds = pages.Select(p => p.Id).ToHashSet();
        var links = await db.WikiLinks
            .AsNoTracking()
            .Where(l => l.UserId == userId && !l.IsDeleted && pageIds.Contains(l.SourcePageId) && (!l.TargetPageId.HasValue || pageIds.Contains(l.TargetPageId.Value)))
            .OrderBy(l => l.LinkType)
            .ThenByDescending(l => l.Strength)
            .ToListAsync();

        return new WikiGraphSyncResultDto
        {
            TopicId = topicId,
            ConceptGraphSnapshotId = graph?.Id,
            SyncStatus = pages.Count == 0 ? "empty" : warnings.Count > 0 ? "ready_with_warnings" : "ready",
            SourceReadiness = sourceReadiness,
            EvidenceStatus = evidenceStatus,
            CreatedPageCount = createdPages,
            UpdatedPageCount = updatedPages,
            CreatedLinkCount = createdLinks,
            Warnings = warnings,
            Graph = BuildGraphDto(topicId, rootPage?.Id, pages, links)
        };
    }

    private static WikiGraphDto BuildGraphDto(
        Guid topicId,
        Guid? focusPageId,
        IReadOnlyList<WikiPage> pages,
        IReadOnlyList<WikiLink> links)
    {
        var pageDtos = pages.Select(ToPageDto).ToList();
        var linkDtos = links.Select(ToLinkDto).ToList();
        var warnings = new List<string>();
        if (pages.Count == 0) warnings.Add("Wiki graph icin henuz sayfa yok.");
        if (links.Count == 0) warnings.Add("Wiki graph icin henuz link/backlink yok.");

        return new WikiGraphDto
        {
            TopicId = topicId,
            FocusPageId = focusPageId,
            GraphStatus = pages.Count == 0 ? "empty" : "ready",
            Pages = pageDtos,
            Links = linkDtos,
            Warnings = warnings,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<ConceptGraphSnapshot?> ResolveConceptGraphForWikiAsync(
        OrkaDbContext db,
        Guid userId,
        TopicScope scope,
        Guid? snapshotId)
    {
        if (snapshotId.HasValue)
        {
            return await db.ConceptGraphSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == snapshotId.Value && g.UserId == userId && (!g.TopicId.HasValue || scope.TreeTopicIds.Contains(g.TopicId.Value)));
        }

        return await db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(g => g.UserId == userId && (!g.TopicId.HasValue || scope.TreeTopicIds.Contains(g.TopicId.Value)))
            .OrderByDescending(g => g.TopicId == scope.CurrentTopicId)
            .ThenByDescending(g => g.TopicId == scope.RootTopicId)
            .ThenByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private static async Task<(WikiPage Page, bool Created, bool Updated)> UpsertWikiPageAsync(
        OrkaDbContext db,
        Guid userId,
        Guid topicId,
        string pageKey,
        string pageType,
        string? conceptKey,
        string? parentConceptKey,
        Guid? parentPageId,
        Guid? conceptGraphSnapshotId,
        string title,
        string safeSummary,
        string sourceReadiness,
        string evidenceStatus,
        int orderIndex,
        DateTime now)
    {
        var page = await db.WikiPages
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TopicId == topicId && p.PageKey == pageKey && !p.IsDeleted);
        if (page == null && !string.IsNullOrWhiteSpace(conceptKey))
        {
            page = await db.WikiPages
                .FirstOrDefaultAsync(p => p.UserId == userId && p.TopicId == topicId && p.ConceptKey == conceptKey && !p.IsDeleted);
        }

        if (page == null)
        {
            page = new WikiPage
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                PageKey = pageKey,
                PageType = pageType,
                ConceptKey = conceptKey,
                ParentConceptKey = parentConceptKey,
                ParentWikiPageId = parentPageId,
                ConceptGraphSnapshotId = conceptGraphSnapshotId,
                Title = CleanText(title, 240) ?? "Wiki Page",
                SafeSummary = CleanText(safeSummary, 1200),
                SourceReadiness = sourceReadiness,
                EvidenceStatus = evidenceStatus,
                MetadataJson = "{}",
                Status = "ready",
                OrderIndex = orderIndex,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.WikiPages.Add(page);
            return (page, true, false);
        }

        var changed = false;
        changed |= SetStringIfDifferent(() => page.PageKey, value => page.PageKey = value, pageKey);
        changed |= SetStringIfDifferent(() => page.PageType, value => page.PageType = value, pageType);
        changed |= SetIfDifferent(() => page.ConceptKey, value => page.ConceptKey = value, conceptKey);
        changed |= SetIfDifferent(() => page.ParentConceptKey, value => page.ParentConceptKey = value, parentConceptKey);
        if (page.ParentWikiPageId != parentPageId)
        {
            page.ParentWikiPageId = parentPageId;
            changed = true;
        }
        if (conceptGraphSnapshotId.HasValue && page.ConceptGraphSnapshotId != conceptGraphSnapshotId)
        {
            page.ConceptGraphSnapshotId = conceptGraphSnapshotId;
            changed = true;
        }
        changed |= SetStringIfDifferent(() => page.Title, value => page.Title = value, CleanText(title, 240) ?? page.Title);
        changed |= SetIfDifferent(() => page.SafeSummary, value => page.SafeSummary = value, CleanText(safeSummary, 1200));
        changed |= SetStringIfDifferent(() => page.SourceReadiness, value => page.SourceReadiness = value, sourceReadiness);
        changed |= SetStringIfDifferent(() => page.EvidenceStatus, value => page.EvidenceStatus = value, evidenceStatus);
        if (page.OrderIndex != orderIndex)
        {
            page.OrderIndex = orderIndex;
            changed = true;
        }
        if (page.Status != "ready")
        {
            page.Status = "ready";
            changed = true;
        }
        if (changed) page.UpdatedAt = now;
        return (page, false, changed);
    }

    private static async Task EnsureSummaryBlockAsync(
        OrkaDbContext db,
        WikiPage page,
        string title,
        string content,
        string sourceBasis,
        string? conceptKey,
        DateTime now)
    {
        var exists = await db.WikiBlocks.AnyAsync(b =>
            b.WikiPageId == page.Id &&
            !b.IsDeleted &&
            b.BlockType == WikiBlockType.Summary &&
            b.Title == title);
        if (exists) return;

        var maxOrder = await db.WikiBlocks
            .Where(b => b.WikiPageId == page.Id && !b.IsDeleted)
            .MaxAsync(b => (int?)b.OrderIndex) ?? 0;
        db.WikiBlocks.Add(new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = page.Id,
            BlockType = WikiBlockType.Summary,
            Title = CleanText(title, 240) ?? "Summary",
            Content = CleanText(content, 1600) ?? string.Empty,
            SourceBasis = NormalizeSourceBasis(sourceBasis),
            ConceptKey = CleanText(conceptKey, 180),
            Visibility = "normal",
            SafetyWarningsJson = "[]",
            OrderIndex = maxOrder + 1,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task<int> EnsureWikiLinkAsync(
        OrkaDbContext db,
        Guid userId,
        Guid topicId,
        Guid sourcePageId,
        Guid targetPageId,
        string targetPageKey,
        string linkType,
        decimal strength,
        string createdBy,
        string safeLabel,
        DateTime now)
    {
        linkType = NormalizeLinkType(linkType);
        var existing = await db.WikiLinks.FirstOrDefaultAsync(l =>
            l.UserId == userId &&
            l.SourcePageId == sourcePageId &&
            l.TargetPageId == targetPageId &&
            l.LinkType == linkType &&
            !l.IsDeleted);
        if (existing != null)
        {
            existing.Strength = ClampStrength(strength);
            existing.SafeLabel = CleanText(safeLabel, 512) ?? existing.SafeLabel;
            existing.TargetPageKey = targetPageKey;
            existing.UpdatedAt = now;
            return 0;
        }

        db.WikiLinks.Add(new WikiLink
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SourcePageId = sourcePageId,
            TargetPageId = targetPageId,
            TargetPageKey = targetPageKey,
            LinkType = linkType,
            Strength = ClampStrength(strength),
            CreatedBy = NormalizeCreatedBy(createdBy),
            SafeLabel = CleanText(safeLabel, 512) ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        });
        return 1;
    }

    private static async Task<int> EnsureSequenceLinksAsync(
        OrkaDbContext db,
        Guid userId,
        Guid topicId,
        IReadOnlyList<LearningConcept> concepts,
        IReadOnlyDictionary<string, WikiPage> pageByConcept,
        DateTime now)
    {
        var ordered = concepts
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Label)
            .Where(c => pageByConcept.ContainsKey(c.StableKey))
            .ToList();
        var created = 0;
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var source = pageByConcept[ordered[i].StableKey];
            var target = pageByConcept[ordered[i + 1].StableKey];
            created += await EnsureWikiLinkAsync(
                db,
                userId,
                topicId,
                source.Id,
                target.Id,
                target.PageKey,
                "plan_sequence",
                0.7m,
                "system",
                $"{source.Title} -> {target.Title} (sira)",
                now);
        }
        return created;
    }

    private static async Task<int> SyncTopicTreePagesAsync(
        OrkaDbContext db,
        Guid userId,
        Guid topicId,
        TopicScope scope,
        IReadOnlyList<Topic> topics,
        WikiPage? currentRootPage,
        string sourceReadiness,
        string evidenceStatus,
        bool createSummaryBlocks,
        DateTime now,
        Action<bool, bool> countPage)
    {
        var createdLinks = 0;
        var orderedTopics = topics
            .OrderBy(t => t.ParentTopicId.HasValue ? 1 : 0)
            .ThenBy(t => t.Order)
            .ThenBy(t => t.CreatedAt)
            .ToList();
        var parentTopicIds = topics
            .Where(t => t.ParentTopicId.HasValue)
            .Select(t => t.ParentTopicId!.Value)
            .ToHashSet();
        var pageByTopic = new Dictionary<Guid, WikiPage>();
        if (currentRootPage != null)
        {
            pageByTopic[currentRootPage.TopicId] = currentRootPage;
        }

        foreach (var topic in orderedTopics)
        {
            var parentPageId = topic.ParentTopicId.HasValue && pageByTopic.TryGetValue(topic.ParentTopicId.Value, out var parentPage)
                ? parentPage.Id
                : topic.Id == topicId ? currentRootPage?.Id : null;
            var pageType = topic.Id == scope.RootTopicId ? "topic_root" : parentTopicIds.Contains(topic.Id) ? "subtopic" : "lesson";
            var order = topic.Id == topicId ? 0 : Math.Max(1, topic.Order + 1);
            (var page, var created, var updated) = await UpsertWikiPageAsync(
                db,
                userId,
                topic.Id,
                BuildTopicPageKey(topic.Id),
                pageType,
                null,
                null,
                parentPageId,
                null,
                topic.Title,
                $"Bu Wiki sayfasi {topic.Title} basligi icin ders notlari, sorular ve tekrar izlerini toplar.",
                sourceReadiness,
                evidenceStatus,
                order,
                now);
            pageByTopic[topic.Id] = page;
            countPage(created, updated);

            if (createSummaryBlocks)
            {
                await EnsureSummaryBlockAsync(db, page, topic.Title, $"Bu sayfa {topic.Title} ile ilgili notlari, kaynak baglantilarini ve pekistirme kayitlarini toplar.", "model_assisted", null, now);
            }

            if (topic.ParentTopicId.HasValue && pageByTopic.TryGetValue(topic.ParentTopicId.Value, out var parent))
            {
                createdLinks += await EnsureWikiLinkAsync(db, userId, topicId, parent.Id, page.Id, page.PageKey, "parent_child", 1m, "system", $"{parent.Title} -> {page.Title}", now);
            }
        }

        return createdLinks;
    }

    private static string BuildTopicPageKey(Guid topicId) => $"topic:{topicId:N}";

    private static string BuildConceptPageKey(string conceptKey)
    {
        var normalized = CleanText(conceptKey, 180) ?? "concept";
        normalized = Regex.Replace(normalized.Trim().ToLowerInvariant(), @"[^a-z0-9_\-:.]+", "-");
        normalized = Regex.Replace(normalized, "-{2,}", "-").Trim('-');
        return $"concept:{(string.IsNullOrWhiteSpace(normalized) ? "concept" : normalized)}";
    }

    private static string? ResolveParentConceptKey(LearningConcept concept, IReadOnlyList<ConceptRelation> relations)
    {
        var prerequisite = relations
            .Where(r => string.Equals(r.TargetConceptKey, concept.StableKey, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RelationType, "prerequisite", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Weight)
            .Select(r => r.SourceConceptKey)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(prerequisite) ? null : prerequisite;
    }

    private static string NormalizeRelationLinkType(string relationType)
    {
        var key = string.IsNullOrWhiteSpace(relationType) ? "related" : relationType.Trim().ToLowerInvariant();
        return key switch
        {
            "prerequisite" or "requires" => "prerequisite",
            "parent_child" or "contains" => "parent_child",
            "misconception_of" => "misconception_of",
            "source_supports" => "source_supports",
            "plan_sequence" => "plan_sequence",
            _ => "related"
        };
    }

    private static string NormalizeEvidenceStatus(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "evidence_insufficient" : value.Trim().ToLowerInvariant();
        return key is "source_grounded" or "wiki_backed" or "mixed" or "degraded" or "stale" or "evidence_insufficient"
            ? key
            : "evidence_insufficient";
    }

    private static string NormalizeSourceBasis(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "model_assisted" : value.Trim().ToLowerInvariant();
        return key is "source_grounded" or "wiki_backed" or "tool_evidence" or "code_output" or "model_assisted" or
            "evidence_insufficient" or "student_manual" or "tutor_generated" or "assessment_verified" or "assessment_signal"
            ? key
            : "model_assisted";
    }

    private static string? CleanText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = Regex.Replace(value.Trim(), @"\s+", " ");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static bool SetIfDifferent(Func<string?> get, Action<string?> set, string? value)
    {
        if (get() == value) return false;
        set(value);
        return true;
    }

    private static bool SetStringIfDifferent(Func<string> get, Action<string> set, string value)
    {
        if (get() == value) return false;
        set(value);
        return true;
    }

    private static WikiGraphPageDto ToPageDto(WikiPage page)
    {
        var visibleBlocks = page.Blocks?.Where(b => !b.IsDeleted).ToArray() ?? [];
        var requiredBlockTypesPresent = HasRequiredWikiBlockType(visibleBlocks);
        var hasLearningContent = !string.IsNullOrWhiteSpace(page.SafeSummary) && visibleBlocks.Length > 0;
        return new WikiGraphPageDto
        {
            Id = page.Id,
            TopicId = page.TopicId,
            ParentWikiPageId = page.ParentWikiPageId,
            PlanStepId = page.PlanStepId,
            PageKey = string.IsNullOrWhiteSpace(page.PageKey) ? page.Id.ToString("N") : page.PageKey,
            PageType = string.IsNullOrWhiteSpace(page.PageType) ? "concept" : page.PageType,
            ConceptKey = page.ConceptKey,
            ParentConceptKey = page.ParentConceptKey,
            Title = page.Title,
            Status = page.Status,
            SourceReadiness = page.SourceReadiness,
            EvidenceStatus = page.EvidenceStatus,
            SafeSummary = page.SafeSummary,
            ContentReadiness = hasLearningContent && requiredBlockTypesPresent ? "ready" : visibleBlocks.Length == 0 ? "skeleton" : "degraded",
            HasLearningContent = hasLearningContent,
            VisibleBlockCount = visibleBlocks.Length,
            RequiredBlockTypesPresent = requiredBlockTypesPresent,
            OrderIndex = page.OrderIndex,
            BlockCount = visibleBlocks.Length,
            Curation = WikiAutoCurationService.BuildSummary(page, visibleBlocks),
            LearningSystemBinding = WikiLearningSystemBindingFactory.From(page, visibleBlocks),
            UpdatedAt = page.UpdatedAt
        };
    }

    private static bool HasRequiredWikiBlockType(IReadOnlyCollection<WikiBlock> blocks) =>
        blocks.Any(b => b.BlockType is WikiBlockType.Summary or WikiBlockType.Concept or WikiBlockType.SourceExcerptSummary or WikiBlockType.TutorExplanation or WikiBlockType.RepairNote or WikiBlockType.MisconceptionNote);

    private static WikiGraphLinkDto ToLinkDto(WikiLink link) => new()
    {
        Id = link.Id,
        SourcePageId = link.SourcePageId,
        TargetPageId = link.TargetPageId,
        TargetPageKey = link.TargetPageKey,
        LinkType = link.LinkType,
        Strength = link.Strength,
        CreatedBy = link.CreatedBy,
        SafeLabel = link.SafeLabel,
        CreatedAt = link.CreatedAt
    };

    private static WikiBlockDto ToBlockDto(WikiBlock block) => new()
    {
        Id = block.Id,
        WikiPageId = block.WikiPageId,
        BlockType = ToSnakeCase(block.BlockType.ToString()),
        Title = block.Title,
        Content = block.Content,
        Source = block.Source,
        SourceBasis = block.SourceBasis,
        ConceptKey = block.ConceptKey,
        MisconceptionKey = block.MisconceptionKey,
        QuizAttemptId = block.QuizAttemptId,
        SourceEvidenceBundleId = block.SourceEvidenceBundleId,
        LearningArtifactId = block.LearningArtifactId,
        TutorTurnStateId = block.TutorTurnStateId,
        Visibility = block.Visibility,
        SafetyWarnings = ParseSafetyWarnings(block.SafetyWarningsJson),
        OrderIndex = block.OrderIndex,
        CreatedAt = block.CreatedAt,
        UpdatedAt = block.UpdatedAt
    };

    private static WikiBlockType NormalizeWikiBlockType(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value)
            ? "manual_note"
            : Regex.Replace(value.Trim(), @"[\s\-]+", "_").ToLowerInvariant();
        return key switch
        {
            "user_note" => WikiBlockType.UserNote,
            "tutor_explanation" => WikiBlockType.TutorExplanation,
            "student_question" => WikiBlockType.StudentQuestion,
            "source_note" => WikiBlockType.SourceNote,
            "source_excerpt_summary" => WikiBlockType.SourceExcerptSummary,
            "worked_example" => WikiBlockType.WorkedExample,
            "misconception_note" => WikiBlockType.MisconceptionNote,
            "repair_note" => WikiBlockType.RepairNote,
            "quiz_result" => WikiBlockType.QuizResult,
            "quiz_review" => WikiBlockType.QuizReview,
            "flashcard_seed" => WikiBlockType.FlashcardSeed,
            "artifact_link" => WikiBlockType.ArtifactLink,
            "checkpoint" => WikiBlockType.Checkpoint,
            "summary" => WikiBlockType.Summary,
            "manual_note" => WikiBlockType.ManualNote,
            "code_trace" => WikiBlockType.CodeTrace,
            "formula" => WikiBlockType.Formula,
            "table" => WikiBlockType.Table,
            "diagram" => WikiBlockType.Diagram,
            _ => WikiBlockType.ManualNote
        };
    }

    private static string BuildDefaultBlockTitle(string? blockType)
    {
        return NormalizeWikiBlockType(blockType) switch
        {
            WikiBlockType.StudentQuestion => "Ogrenci sorusu",
            WikiBlockType.MisconceptionNote => "Takilma notu",
            WikiBlockType.RepairNote => "Pekistirme notu",
            WikiBlockType.SourceNote => "Kaynak notu",
            WikiBlockType.QuizReview => "Quiz degerlendirmesi",
            WikiBlockType.WorkedExample => "Cozumlu ornek",
            _ => "Wiki notu"
        };
    }

    private static string NormalizeVisibility(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "normal" : value.Trim().ToLowerInvariant();
        return key is "normal" or "collapsed" or "highlighted" or "private" ? key : "normal";
    }

    private static string SanitizePublicText(string? value, int maxLength, out IReadOnlyList<string> warnings)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            warnings = found;
            return string.Empty;
        }

        var cleaned = value.Trim();
        foreach (var marker in SensitiveMarkers)
        {
            if (cleaned.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = Regex.Replace(cleaned, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase);
                found.Add($"redacted_{ToSnakeCase(marker)}");
            }
        }

        cleaned = Regex.Replace(cleaned, @"\r\n?", "\n");
        cleaned = Regex.Replace(cleaned, @"[ \t]{2,}", " ");
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength];
            found.Add("content_clipped");
        }

        warnings = found;
        return cleaned;
    }

    private static IReadOnlyList<string> ParseSafetyWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var snake = Regex.Replace(value.Trim(), "([a-z0-9])([A-Z])", "$1_$2");
        return Regex.Replace(snake, @"[\s\-]+", "_").ToLowerInvariant();
    }

    private static readonly string[] SensitiveMarkers =
    [
        "rawProviderPayload",
        "rawToolPayload",
        "rawSourceChunk",
        "rawAnswerRows",
        "hiddenPrompt",
        "systemPrompt",
        "developerPrompt",
        "debugTrace",
        "stackTrace",
        "localPath",
        "apiKey",
        "secret"
    ];

    private static string NormalizeLinkType(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "related" : value.Trim().ToLowerInvariant();
        return key is "parent_child" or "prerequisite" or "related" or "misconception_of" or "source_supports" or "plan_sequence" or "manual" or "mention"
            ? key
            : "related";
    }

    private static string NormalizeCreatedBy(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "manual" : value.Trim().ToLowerInvariant();
        return key is "system" or "tutor" or "source" or "student" or "manual" ? key : "manual";
    }

    private static decimal ClampStrength(decimal value)
    {
        if (value <= 0m) return 0.1m;
        return value > 1m ? 1m : decimal.Round(value, 4);
    }

    private static string BuildSafeLinkLabel(string? requested, string sourceTitle, string targetTitle, string linkType)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim().Length > 240 ? requested.Trim()[..240] : requested.Trim();
        return $"{sourceTitle} -> {targetTitle} ({linkType})";
    }

    private static IReadOnlyList<Guid> GetWikiReadTopicIds(TopicScope topicScope)
    {
        if (!topicScope.HasDescendants)
            return [topicScope.CurrentTopicId];

        return new[] { topicScope.CurrentTopicId }
            .Concat(topicScope.DescendantTopicIds)
            .Distinct()
            .ToArray();
    }

    private static string PreserveDocCitationTags(string summary, string source)
    {
        var sourceTags = Regex.Matches(source, @"\[doc:[0-9a-fA-F-]+:p\d+\]", RegexOptions.IgnoreCase)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sourceTags.Count == 0) return summary;

        var missing = sourceTags
            .Where(tag => !summary.Contains(tag, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();
        if (missing.Count == 0) return summary;

        return $"{summary.TrimEnd()}\n\nKaynak etiketleri: {string.Join(" ", missing)}";
    }

    private static string EnsureMarkdownIntegrity(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        // 1. Açık kalan kod bloklarını (```) kapat
        int codeBlockCount = 0;
        int lastIdx = 0;
        while ((lastIdx = content.IndexOf("```", lastIdx)) != -1)
        {
            codeBlockCount++;
            lastIdx += 3;
        }

        if (codeBlockCount % 2 != 0)
        {
            content += "\n```"; // Eksik bloğu kapat
        }

        // 2. Basit HTML/Tablo tag koruması (isteğe bağlı genişletilebilir)
        // Şimdilik sadece Markdown hiyerarşisine odaklanıyoruz.

        return content;
    }

    private static string TruncateTitle(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "Başlıksız";
        if (text.Length <= maxLength) return text;
        return text[..maxLength].TrimEnd() + "...";
    }
}
