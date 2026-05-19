using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Core.Services;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class WikiEvidenceService : IWikiEvidenceService
{
    private readonly OrkaDbContext _db;
    private readonly ILearningSourceService _sources;
    private readonly IConceptMasteryService _mastery;
    private readonly IKnowledgeTracingService _knowledgeTracing;
    private readonly ILearningSignalService _signals;
    private readonly ILogger<WikiEvidenceService> _logger;

    public WikiEvidenceService(
        OrkaDbContext db,
        ILearningSourceService sources,
        IConceptMasteryService mastery,
        IKnowledgeTracingService knowledgeTracing,
        ILearningSignalService signals,
        ILogger<WikiEvidenceService> logger)
    {
        _db = db;
        _sources = sources;
        _mastery = mastery;
        _knowledgeTracing = knowledgeTracing;
        _signals = signals;
        _logger = logger;
    }

    public async Task<WikiEvidenceBundleDto> BuildAsync(
        WikiLearningRequestDto request,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == request.UserId, ct)
            ?? throw new InvalidOperationException("Konu bulunamadı.");

        var sources = await _sources.GetTopicSourcesAsync(request.UserId, request.TopicId, ct);
        var readySources = sources.Where(s => string.Equals(s.Status, "ready", StringComparison.OrdinalIgnoreCase)).ToList();

        var sourceChunks = Array.Empty<TopicSourceEvidenceDto>() as IReadOnlyList<TopicSourceEvidenceDto>;
        try
        {
            sourceChunks = await _sources.RetrieveTopicEvidenceAsync(
                request.UserId,
                request.TopicId,
                request.Question,
                8,
                request.SourceId,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WikiV2] Topic source retrieval skipped. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }

        var wikiBlocks = await BuildWikiBlocksAsync(request, ct);
        var graph = await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == request.UserId && s.TopicId == request.TopicId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id })
            .FirstOrDefaultAsync(ct);

        var graphConcepts = graph == null
            ? []
            : await _db.LearningConcepts
                .AsNoTracking()
                .Where(c => c.ConceptGraphSnapshotId == graph.Id)
                .OrderBy(c => c.Order)
                .Select(c => c.Label)
                .Take(8)
                .ToListAsync(ct);

        var masteries = (await _mastery.GetRecentMasteryAsync(request.UserId, request.TopicId, 8, ct)).ToList();
        var states = (await _knowledgeTracing.GetRecentStatesAsync(request.UserId, request.TopicId, 8, ct)).ToList();
        var weakConcepts = masteries
            .Where(m => m.MasteryScore < 0.65m || m.Confidence < 0.60m || !string.Equals(m.RemediationNeed, "none", StringComparison.OrdinalIgnoreCase))
            .Select(m => string.IsNullOrWhiteSpace(m.Label) ? m.ConceptKey : $"{m.Label} ({m.RemediationNeed})")
            .Concat(states
                .Where(s => s.MasteryProbability < 0.65m || s.Confidence < 0.60m)
                .Select(s => string.IsNullOrWhiteSpace(s.Label) ? s.ConceptKey : $"{s.Label} (kanıt düşük)"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var recommendations = await SafeRecommendationsAsync(request.UserId, request.TopicId, ct);
        var citations = BuildCitations(sourceChunks, wikiBlocks);
        var bestState = states.OrderByDescending(s => s.LastEvidenceAt ?? DateTimeOffset.MinValue).FirstOrDefault();
        var bestMastery = masteries.OrderByDescending(m => m.Attempts).FirstOrDefault();
        var latestSourceQuality = await _db.SourceQualityReports
            .AsNoTracking()
            .Where(r => r.UserId == request.UserId && r.TopicId == request.TopicId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        var latestRag = await _db.RagEvaluationRuns
            .AsNoTracking()
            .Where(r => r.UserId == request.UserId && r.TopicId == request.TopicId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var retrievalHealth = sourceChunks.Count == 0 ? (readySources.Count == 0 ? "no_source" : "source_retrieval_empty") :
            sourceChunks.Any(c => c.QualityStatus == "low_confidence") ? "low_confidence" :
            "healthy";
        var citationCoverage = latestSourceQuality?.CitationCoverage ?? 0m;
        var unsupportedCitationCount = latestSourceQuality?.UnsupportedCitationCount ?? 0;
        var citationMissingCount = latestSourceQuality?.CitationMissingCount ?? 0;
        var scopedRetrievedSourceCount = sourceChunks.Select(c => c.SourceId).Distinct().Count();
        var effectiveSourceCount = Math.Max(sources.Count, scopedRetrievedSourceCount);
        var effectiveReadySourceCount = Math.Max(readySources.Count, scopedRetrievedSourceCount);
        var evidenceQuality = EvidenceQualityEvaluator.Build(
            effectiveSourceCount,
            effectiveReadySourceCount,
            sourceChunks.Count,
            citationCoverage,
            unsupportedCitationCount,
            citationMissingCount,
            retrievalHealth,
            latestSourceQuality?.CitationCoverageStatus ?? (sourceChunks.Count > 0 ? "unverified" : "unknown"));

        return new WikiEvidenceBundleDto
        {
            UserId = request.UserId,
            TopicId = request.TopicId,
            TopicTitle = topic.Title,
            ConceptGraphSnapshotId = graph?.Id,
            SourceChunks = sourceChunks,
            WikiBlocks = wikiBlocks,
            Citations = citations,
            ActiveConcepts = graphConcepts,
            WeakConcepts = weakConcepts,
            Recommendations = recommendations,
            LearnerState = BuildLearnerState(bestMastery, bestState, weakConcepts.Count),
            MasteryProbability = bestState?.MasteryProbability ?? bestMastery?.MasteryScore,
            Confidence = bestState?.Confidence ?? bestMastery?.Confidence,
            SourceCount = sources.Count,
            ReadySourceCount = readySources.Count,
            CitationHealth = citations.Count > 0 ? "healthy" : "empty",
            LatestRetrievalRunId = sourceChunks.FirstOrDefault()?.RetrievalRunId,
            RetrievalHealth = retrievalHealth,
            RagQualityStatus = latestRag?.QualityStatus ?? latestSourceQuality?.QualityStatus ?? "unknown",
            CitationCoverage = citationCoverage,
            UnsupportedCitationCount = unsupportedCitationCount,
            EvidenceQuality = evidenceQuality
        };
    }

    public async Task<WikiWorkspaceStateDto> GetWorkspaceStateAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var request = new WikiLearningRequestDto
        {
            UserId = userId,
            TopicId = topicId,
            Question = "workspace-state"
        };
        var evidence = await BuildAsync(request, ct);
        var pageStats = await _db.WikiPages
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == topicId && !p.IsDeleted)
            .Select(p => new { BlockCount = p.Blocks.Count(b => !b.IsDeleted) })
            .ToListAsync(ct);

        return new WikiWorkspaceStateDto
        {
            TopicId = topicId,
            TopicTitle = evidence.TopicTitle,
            WikiPageCount = pageStats.Count,
            WikiBlockCount = pageStats.Sum(p => p.BlockCount),
            SourceCount = evidence.SourceCount,
            ReadySourceCount = evidence.ReadySourceCount,
            CitationHealth = evidence.CitationHealth,
            RagQualityStatus = evidence.RagQualityStatus,
            RetrievalHealth = evidence.RetrievalHealth,
            CitationCoverage = evidence.CitationCoverage,
            UnsupportedCitationCount = evidence.UnsupportedCitationCount,
            EvidenceQuality = evidence.EvidenceQuality,
            ActiveConcepts = evidence.ActiveConcepts,
            WeakConcepts = evidence.WeakConcepts,
            RecommendedActions = evidence.Recommendations,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<IReadOnlyList<WikiBlockEvidenceDto>> BuildWikiBlocksAsync(
        WikiLearningRequestDto request,
        CancellationToken ct)
    {
        var query = _db.WikiBlocks
            .AsNoTracking()
            .Include(b => b.WikiPage)
            .Where(b =>
                b.WikiPage.TopicId == request.TopicId &&
                b.WikiPage.UserId == request.UserId &&
                !b.IsDeleted &&
                !b.WikiPage.IsDeleted);

        if (request.ActivePageId.HasValue)
        {
            query = query.Where(b => b.WikiPageId == request.ActivePageId.Value);
        }

        var blocks = await query
            .Where(b => !string.IsNullOrWhiteSpace(b.Content))
            .OrderBy(b => b.WikiPage.OrderIndex)
            .ThenBy(b => b.OrderIndex)
            .Take(200)
            .ToListAsync(ct);

        return blocks
            .Select(b => new WikiBlockEvidenceDto
            {
                PageId = b.WikiPageId,
                BlockId = b.Id,
                PageTitle = b.WikiPage.Title,
                BlockTitle = string.IsNullOrWhiteSpace(b.Title) ? b.WikiPage.Title : b.Title,
                Content = Trim(b.Content, 1600),
                Score = ScoreText($"{b.Title} {b.Content}", request.Question)
            })
            .OrderByDescending(b => b.Score)
            .ThenBy(b => b.PageTitle)
            .Take(8)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> SafeRecommendationsAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        try
        {
            var items = await _signals.GetRecommendationsAsync(userId, topicId, ct);
            return items.Select(i => i.Title).Where(t => !string.IsNullOrWhiteSpace(t)).Take(5).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WikiV2] Recommendations unavailable. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return [];
        }
    }

    private static IReadOnlyList<CitationDto> BuildCitations(
        IReadOnlyList<TopicSourceEvidenceDto> sourceChunks,
        IReadOnlyList<WikiBlockEvidenceDto> wikiBlocks)
    {
        var sourceCitations = sourceChunks.Select(c => new CitationDto(
            c.CitationId,
            "document",
            c.SourceId,
            c.PageNumber,
            $"{c.SourceTitle} / s.{c.PageNumber}",
            null,
            c.Score,
            c.ChunkId,
            c.SourceTopicId,
            c.SourceTopicTitle,
            c.ScopeRelation,
            c.RetrievalScope));
        var wikiCitations = wikiBlocks.Select(b => new CitationDto(
            b.CitationId,
            "wiki",
            b.PageId,
            null,
            $"{b.PageTitle} / {b.BlockTitle}",
            null,
            b.Score));

        return sourceCitations.Concat(wikiCitations)
            .GroupBy(c => c.CitationId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(16)
            .ToList();
    }

    private static string BuildLearnerState(ConceptMasteryDto? mastery, KnowledgeTracingStateDto? state, int weakConceptCount)
    {
        if (state != null)
        {
            return state.EvidenceCount < 3 || state.Confidence < 0.60m
                ? "evidence_insufficient"
                : state.MasteryProbability >= 0.75m ? "ready_for_challenge" : "needs_scaffold";
        }

        if (mastery != null)
        {
            return mastery.Confidence < 0.60m
                ? "evidence_insufficient"
                : mastery.MasteryScore >= 0.75m ? "ready_for_challenge" : "needs_scaffold";
        }

        return weakConceptCount > 0 ? "needs_scaffold" : "unknown";
    }

    private static double ScoreText(string text, string question)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(question)) return 0;
        var tokens = question
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .Distinct()
            .ToList();
        if (tokens.Count == 0) return 0.1;
        var lower = text.ToLowerInvariant();
        return Math.Round(tokens.Count(t => lower.Contains(t, StringComparison.OrdinalIgnoreCase)) / (double)tokens.Count, 4);
    }

    private static string Trim(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars] + "\n[...kırpıldı]";
}

public sealed class WikiAnswerPolicyEngine : IWikiAnswerPolicyEngine
{
    public WikiAnswerPolicyDto BuildPolicy(WikiLearningRequestDto request, WikiEvidenceBundleDto evidence)
    {
        if (request.SourceId.HasValue && evidence.SourceChunks.Count == 0)
        {
            return new WikiAnswerPolicyDto
            {
                CanAnswer = false,
                RequiresCitation = true,
                GroundingStatus = "source_retrieval_empty",
                FallbackReason = "source_retrieval_empty",
                UserSafeMessage = "Bu kaynakta soruna net dayanak olacak bir bölüm bulamadım. İstersen soruyu biraz daha daraltabiliriz.",
                PromptBlock = "Kaynak seçildi ama ilgili parça bulunamadı; genel bilgi uydurma."
            };
        }

        if (evidence.SourceChunks.Count > 0)
        {
            return new WikiAnswerPolicyDto
            {
                CanAnswer = true,
                RequiresCitation = true,
                GroundingStatus = "source_grounded",
                PromptBlock = BuildPromptBlock(evidence, "Belge parçaları ana dayanak. Her olgusal iddianın sonuna [doc:...:p...] etiketi ekle.")
            };
        }

        if (evidence.WikiBlocks.Count > 0)
        {
            return new WikiAnswerPolicyDto
            {
                CanAnswer = true,
                RequiresCitation = true,
                GroundingStatus = "wiki_grounded",
                PromptBlock = BuildPromptBlock(evidence, "Wiki blokları ana dayanak. Her olgusal iddianın sonuna [wiki:page:block] etiketi ekle.")
            };
        }

        return new WikiAnswerPolicyDto
        {
            CanAnswer = false,
            RequiresCitation = false,
            GroundingStatus = "no_source",
            FallbackReason = "source_retrieval_empty",
            UserSafeMessage = "Bu bilgi mevcut kaynaklarda net görünmüyor. Önce ilgili belgeyi yükleyelim ya da Wiki notu oluşturalım; sonra kaynağa bağlı anlatabilirim.",
            PromptBlock = "Kaynak yok; cevap uydurma."
        };
    }

    private static string BuildPromptBlock(WikiEvidenceBundleDto evidence, string rule)
    {
        var scaffold = evidence.LearnerState is "needs_scaffold" or "evidence_insufficient"
            ? "Öğrenci kanıtı düşük görünüyor; cevabı kısa adımlar, örnek ve mini kontrol sorusuyla scaffold et."
            : "Öğrenci hazır görünüyorsa cevabı daha yoğun ve pratik odaklı tut.";
        return $"""
            [WIKI ANSWER POLICY]
            Grounding: {rule}
            Learner state: {evidence.LearnerState}
            Pedagogy: {scaffold}
            Kaynakta olmayan bilgiyi kaynağa mal etme.
            """;
    }
}

public sealed class WikiCitationGuard : IWikiCitationGuard
{
    private static readonly Regex CitationPattern = new(@"\[(doc|wiki):[^\]]+\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public WikiCitationGuardResultDto Apply(string answer, WikiEvidenceBundleDto evidence, WikiAnswerPolicyDto policy)
    {
        var citations = evidence.Citations;
        var repaired = false;
        var finalAnswer = string.IsNullOrWhiteSpace(answer) ? policy.UserSafeMessage : answer.Trim();
        var validCitationIds = citations.Select(c => c.CitationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var answerCitations = CitationPattern.Matches(finalAnswer)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!policy.CanAnswer)
        {
            finalAnswer = policy.UserSafeMessage;
        }
        else if (policy.RequiresCitation && answerCitations.Count == 0 && citations.Count > 0)
        {
            finalAnswer = $"{finalAnswer}\n\nKaynak: {citations[0].CitationId}";
            repaired = true;
            answerCitations.Add(citations[0].CitationId);
        }

        var unsupported = answerCitations
            .Where(c => !validCitationIds.Contains(c))
            .ToArray();
        var supportedCitationCount = answerCitations.Count(c => validCitationIds.Contains(c));
        var missing = policy.RequiresCitation && supportedCitationCount == 0;
        var healthy = !policy.RequiresCitation || (!missing && unsupported.Length == 0);
        var coverageStatus = unsupported.Length > 0 ? "citation_unsupported" :
            missing ? "citation_missing" :
            "healthy";
        var warnings = new List<string>();
        if (missing) warnings.Add("citation_missing");
        if (unsupported.Length > 0) warnings.Add("citation_unsupported");
        if (evidence.RetrievalHealth is "source_retrieval_empty" or "low_confidence")
            warnings.Add(evidence.RetrievalHealth);

        return new WikiCitationGuardResultDto
        {
            Answer = finalAnswer,
            IsHealthy = healthy,
            Repaired = repaired,
            CitationCoverageStatus = coverageStatus,
            Metadata = new ChatResponseMetadata
            {
                Citations = citations,
                GroundingMode = policy.GroundingStatus,
                GroundingStatus = policy.GroundingStatus,
                FallbackReason = policy.FallbackReason,
                SourceConfidence = citations.Count > 0 ? citations.Max(c => c.Confidence ?? 0) : null,
                RagQualityStatus = evidence.RagQualityStatus,
                EvidenceQuality = evidence.EvidenceQuality,
                EvidenceSummary = new EvidenceSummaryDto(
                    ReadyToolCount: 0,
                    SourceCount: evidence.SourceChunks.Count + evidence.WikiBlocks.Count,
                    GroundingStatus: policy.GroundingStatus,
                    LearnerEvidenceStatus: evidence.LearnerState),
                ProviderWarnings = warnings,
                MasteryProbability = evidence.MasteryProbability,
                Confidence = evidence.Confidence,
                ActiveConceptKey = evidence.ActiveConcepts.FirstOrDefault()
            }
        };
    }
}

public sealed class WikiArtifactService : IWikiArtifactService
{
    private readonly OrkaDbContext _db;
    private readonly ILearningArtifactService _learningArtifacts;

    public WikiArtifactService(OrkaDbContext db, ILearningArtifactService learningArtifacts)
    {
        _db = db;
        _learningArtifacts = learningArtifacts;
    }

    public async Task<IReadOnlyList<TeachingArtifactDto>> BuildArtifactsAsync(
        WikiLearningRequestDto request,
        WikiEvidenceBundleDto evidence,
        WikiCitationGuardResultDto guardedAnswer,
        CancellationToken ct = default)
    {
        if (evidence.Citations.Count == 0) return Array.Empty<TeachingArtifactDto>();

        var lines = evidence.Citations.Take(5).Select(c => $"- {c.Label ?? c.CitationId} `{c.CitationId}`");
        var artifact = new TeachingArtifact
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            ArtifactType = "retrieval_card",
            Title = "Kaynak dayanak kartı",
            Content = $"**Bu cevap şu dayanaklarla üretildi:**\n\n{string.Join("\n", lines)}",
            RenderFormat = "markdown",
            Status = "ready",
            Provider = "wiki-v2",
            MetadataJson = JsonSerializer.Serialize(new
            {
                guardedAnswer.CitationCoverageStatus,
                evidence.CitationHealth,
                citationCount = evidence.Citations.Count
            }),
            CreatedAt = DateTime.UtcNow
        };

        _db.TeachingArtifacts.Add(artifact);
        await _db.SaveChangesAsync(ct);
        await _learningArtifacts.MirrorTeachingArtifactAsync(request.UserId, artifact, origin: "wiki", ct: ct);

        return new[]
        {
            new TeachingArtifactDto
            {
                Id = artifact.Id,
                UserId = artifact.UserId,
                TopicId = artifact.TopicId,
                SessionId = artifact.SessionId,
                ArtifactType = artifact.ArtifactType,
                Title = artifact.Title,
                Content = artifact.Content,
                RenderFormat = artifact.RenderFormat,
                Status = artifact.Status,
                Provider = artifact.Provider,
                ExternalUrl = artifact.ExternalUrl,
                RenderError = artifact.RenderError,
                RenderedAt = artifact.RenderedAt,
                CreatedAt = artifact.CreatedAt
            }
        };
    }
}

public sealed class WikiLearningAssistant : IWikiLearningAssistant
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex DocCitationPattern = new(@"\[doc:(?<sourceId>[^:\]]+):p(?<page>\d+)(?::c(?<chunk>\d+))?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly OrkaDbContext _db;
    private readonly IWikiEvidenceService _evidence;
    private readonly IWikiAnswerPolicyEngine _policy;
    private readonly IWikiCitationGuard _citationGuard;
    private readonly IWikiArtifactService _artifacts;
    private readonly IAIAgentFactory _factory;
    private readonly ILearningEventNormalizer _events;
    private readonly ILogger<WikiLearningAssistant> _logger;

    public WikiLearningAssistant(
        OrkaDbContext db,
        IWikiEvidenceService evidence,
        IWikiAnswerPolicyEngine policy,
        IWikiCitationGuard citationGuard,
        IWikiArtifactService artifacts,
        IAIAgentFactory factory,
        ILearningEventNormalizer events,
        ILogger<WikiLearningAssistant> logger)
    {
        _db = db;
        _evidence = evidence;
        _policy = policy;
        _citationGuard = citationGuard;
        _artifacts = artifacts;
        _factory = factory;
        _events = events;
        _logger = logger;
    }

    public async IAsyncEnumerable<WikiStreamEventDto> StreamAsync(
        WikiLearningRequestDto request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messageId = Guid.NewGuid();
        var evidence = await _evidence.BuildAsync(request, ct);
        var policy = _policy.BuildPolicy(request, evidence);
        var rawAnswer = policy.CanAnswer
            ? await GenerateAnswerAsync(request, evidence, policy, ct)
            : policy.UserSafeMessage;

        var guarded = _citationGuard.Apply(rawAnswer, evidence, policy);
        var artifacts = await _artifacts.BuildArtifactsAsync(request, evidence, guarded, ct);
        guarded.Metadata.ArtifactIds = artifacts.Select(a => a.Id).ToList();
        guarded.Metadata.ArtifactSummaries = artifacts.Select(a => new ArtifactSummaryDto(
            a.Id,
            a.ArtifactType,
            a.Title,
            a.Status,
            a.RenderFormat,
            a.Provider,
            a.ExternalUrl)).ToList();

        await RecordCitationChecksAsync(request, evidence, guarded, ct);
        await RecordLearningEventAsync(request, guarded, artifacts, ct);

        yield return new WikiStreamEventDto { Type = "token", Content = guarded.Answer };
        if (guarded.Metadata.Citations.Count > 0)
        {
            yield return new WikiStreamEventDto { Type = "citation", Citations = guarded.Metadata.Citations };
        }

        foreach (var artifact in artifacts)
        {
            yield return new WikiStreamEventDto
            {
                Type = "artifact_ready",
                ArtifactId = artifact.Id,
                ArtifactType = artifact.ArtifactType
            };
        }

        yield return new WikiStreamEventDto { Type = "metadata", Metadata = guarded.Metadata };
        yield return new WikiStreamEventDto
        {
            Type = "final",
            MessageId = messageId,
            GroundingStatus = guarded.Metadata.GroundingStatus
        };
    }

    private async Task<string> GenerateAnswerAsync(
        WikiLearningRequestDto request,
        WikiEvidenceBundleDto evidence,
        WikiAnswerPolicyDto policy,
        CancellationToken ct)
    {
        var systemPrompt = """
            Sen Orka Wiki Learning Assistant'sın.
            Görevin kaynaklara bağlı, öğrenci seviyesine duyarlı ve öğretici cevap vermek.
            Kaynakta olmayan bilgiyi kaynak varmış gibi anlatma.
            Her olgusal iddiada verilen citation etiketlerinden birini kullan.
            Eğer kanıt zayıfsa açıkça söyle ve öğrenciyi güvenli sonraki adıma yönlendir.
            """;

        var sourceEvidence = string.Join("\n\n", evidence.SourceChunks.Take(8).Select(c =>
            $"{c.CitationId} {c.SourceTitle} / sayfa {c.PageNumber}\n{Trim(c.Text, 900)}"));
        var wikiEvidence = string.Join("\n\n", evidence.WikiBlocks.Take(8).Select(b =>
            $"{b.CitationId} {b.PageTitle} / {b.BlockTitle}\n{Trim(b.Content, 900)}"));
        var weak = evidence.WeakConcepts.Count == 0 ? "(belirgin zayıf kavram yok)" : string.Join(", ", evidence.WeakConcepts);
        var concepts = evidence.ActiveConcepts.Count == 0 ? "(concept graph yok veya henüz hazır değil)" : string.Join(", ", evidence.ActiveConcepts);

        var userPrompt = $$"""
            Soru:
            {{request.Question}}

            {{policy.PromptBlock}}

            [KAVRAM VE ÖĞRENCİ DURUMU]
            Topic: {{evidence.TopicTitle}}
            Aktif kavramlar: {{concepts}}
            Zayıf/kanıtı düşük kavramlar: {{weak}}
            Öğrenci durumu: {{evidence.LearnerState}}

            [BELGE KANITLARI]
            {{(string.IsNullOrWhiteSpace(sourceEvidence) ? "(yok)" : sourceEvidence)}}

            [WIKI KANITLARI]
            {{(string.IsNullOrWhiteSpace(wikiEvidence) ? "(yok)" : wikiEvidence)}}

            Cevabı Türkçe ver. Gerekiyorsa mini kontrol sorusu ile bitir.
            """;

        try
        {
            return await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, userPrompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[WikiV2] Assistant generation failed. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return policy.UserSafeMessage;
        }
    }

    private async Task RecordLearningEventAsync(
        WikiLearningRequestDto request,
        WikiCitationGuardResultDto guarded,
        IReadOnlyList<TeachingArtifactDto> artifacts,
        CancellationToken ct)
    {
        try
        {
            await _events.RecordSignalEventAsync(
                request.UserId,
                request.TopicId,
                request.SessionId,
                "WikiQuestionAsked",
                skillTag: guarded.Metadata.ActiveConceptKey,
                payloadJson: JsonSerializer.Serialize(new
                {
                    request.Mode,
                    request.SourceId,
                    request.ActivePageId,
                    groundingStatus = guarded.Metadata.GroundingStatus,
                    citationCount = guarded.Metadata.Citations.Count,
                    artifactIds = artifacts.Select(a => a.Id).ToArray(),
                    citationCoverageStatus = guarded.CitationCoverageStatus
                }, JsonOptions),
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WikiV2] Learning event skipped. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private async Task RecordCitationChecksAsync(
        WikiLearningRequestDto request,
        WikiEvidenceBundleDto evidence,
        WikiCitationGuardResultDto guarded,
        CancellationToken ct)
    {
        if (evidence.SourceChunks.Count == 0) return;

        try
        {
            var matches = DocCitationPattern.Matches(guarded.Answer);
            var checks = new List<SourceCitationCheck>();
            if (matches.Count == 0)
            {
                checks.Add(new SourceCitationCheck
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    TopicId = request.TopicId,
                    SessionId = request.SessionId,
                    SourceRetrievalRunId = evidence.LatestRetrievalRunId,
                    SourceId = request.SourceId,
                    CitationId = string.Empty,
                    SourceType = "document",
                    Answer = WikiCitationText.TrimForStorage(guarded.Answer, 4000),
                    ClaimText = string.Empty,
                    CheckStatus = "citation_missing",
                    Confidence = 0m,
                    Reason = "wiki_answer_contains_no_document_citation",
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                foreach (Match match in matches)
                {
                    var sourceText = match.Groups["sourceId"].Value;
                    var sourceId = Guid.TryParse(sourceText, out var parsedSourceId) ? parsedSourceId : (Guid?)null;
                    var page = int.TryParse(match.Groups["page"].Value, out var pageNumber) ? pageNumber : (int?)null;
                    var chunk = int.TryParse(match.Groups["chunk"].Value, out var chunkIndex) ? chunkIndex : (int?)null;
                    var supported = sourceId.HasValue && page.HasValue && evidence.SourceChunks.Any(c =>
                        c.SourceId == sourceId.Value &&
                        c.PageNumber == page.Value &&
                        (!chunk.HasValue || c.ChunkIndex == chunk.Value));
                    var sourceChunk = supported
                        ? evidence.SourceChunks.First(c => c.SourceId == sourceId!.Value &&
                                                           c.PageNumber == page!.Value &&
                                                           (!chunk.HasValue || c.ChunkIndex == chunk.Value))
                        : null;

                    checks.Add(new SourceCitationCheck
                    {
                        Id = Guid.NewGuid(),
                        UserId = request.UserId,
                        TopicId = request.TopicId,
                        SessionId = request.SessionId,
                        SourceRetrievalRunId = evidence.LatestRetrievalRunId,
                        SourceId = sourceId ?? request.SourceId,
                        SourceChunkId = sourceChunk?.ChunkId,
                        CitationId = match.Value,
                        SourceType = "document",
                        PageNumber = page,
                        ChunkIndex = chunk,
                        Answer = WikiCitationText.TrimForStorage(guarded.Answer, 4000),
                        ClaimText = WikiCitationText.ExtractClaimNearCitation(guarded.Answer, match.Index),
                        CheckStatus = supported ? "supported" : "citation_unsupported",
                        Confidence = supported ? sourceChunk!.FusedScore : 0m,
                        Reason = supported ? "citation_matches_wiki_evidence_bundle" : "citation_not_in_wiki_evidence_bundle",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _db.SourceCitationChecks.AddRange(checks);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WikiV2] Citation check skipped. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(request.TopicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private static string Trim(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars] + "\n[...kırpıldı]";
}
