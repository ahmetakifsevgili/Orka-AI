using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnyAscii;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class ConceptGraphBuilder : IConceptGraphBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly IConceptGraphQualityService? _quality;
    private readonly IResourceConceptAlignmentService? _alignment;
    private readonly IConceptScopePlanner _scopePlanner;
    private readonly ILogger<ConceptGraphBuilder> _logger;

    public ConceptGraphBuilder(
        OrkaDbContext db,
        IRedisMemoryService? redis,
        ILogger<ConceptGraphBuilder> logger,
        IConceptGraphQualityService? quality = null,
        IResourceConceptAlignmentService? alignment = null,
        IConceptScopePlanner? scopePlanner = null)
    {
        _db = db;
        _redis = redis;
        _quality = quality;
        _alignment = alignment;
        _scopePlanner = scopePlanner ?? new ConceptScopePlanner();
        _logger = logger;
    }

    public async Task<ConceptGraphBuildResultDto> BuildOrLoadAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        CompressedPlanResearchContextDto compressedContext,
        CancellationToken ct = default)
    {
        var intentHash = ComputeIntentHash(approvedResearchIntent, approvedMainTopic, approvedFocusArea);
        var cacheKey = $"orka:v11:concept-graph:{intentHash}";
        var sourceBundleCacheKey = $"orka:v11:source-bundle:{intentHash}";

        var existing = await _db.ConceptGraphSnapshots
            .Include(s => s.Concepts)
            .Include(s => s.Relations)
            .Include(s => s.Outcomes)
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && s.IntentHash == intentHash)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing != null)
        {
            var graph = DeserializeGraph(existing.GraphJson) ?? ToDto(existing);
            graph.SnapshotId = existing.Id;
            await TryCacheSourceBundleAsync(
                sourceBundleCacheKey,
                intentHash,
                graph.SourceBundleHash,
                approvedResearchIntent,
                topicTitle,
                approvedMainTopic,
                approvedFocusArea,
                compressedContext);
            var existingQuality = await EvaluateQualityAsync(userId, topicId, planRequestId, graph, ct);
            await AlignSourcesAsync(userId, topicId, graph, ct);
            return new ConceptGraphBuildResultDto
            {
                Graph = graph,
                SnapshotId = existing.Id,
                CacheKey = cacheKey,
                SourceBundleCacheKey = sourceBundleCacheKey,
                QualityRunId = existingQuality?.Id,
                QualityStatus = existingQuality?.QualityStatus ?? "unknown",
                QualityCacheKey = $"orka:v10:graph-quality:{existing.Id:N}",
                CacheHit = true
            };
        }

        ConceptGraphDto? cachedGraph = null;
        if (_redis != null)
        {
            try
            {
                var cached = await _redis.GetJsonAsync(cacheKey);
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    cachedGraph = JsonSerializer.Deserialize<ConceptGraphDto>(cached, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[ConceptGraph] Redis read skipped. KeyRef={KeyRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeTextRef(cacheKey, "cache"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        var graphToSave = cachedGraph ?? BuildGraph(
            intentHash,
            approvedResearchIntent,
            topicTitle,
            approvedMainTopic,
            approvedFocusArea,
            compressedContext);

        var snapshot = await SaveSnapshotAsync(userId, topicId, planRequestId, graphToSave, ct);
        graphToSave.SnapshotId = snapshot.Id;

        if (_redis != null)
        {
            try
            {
                await _redis.SetJsonAsync(cacheKey, JsonSerializer.Serialize(graphToSave, JsonOptions), TimeSpan.FromHours(12));
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[ConceptGraph] Redis write skipped. KeyRef={KeyRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeTextRef(cacheKey, "cache"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        await TryCacheSourceBundleAsync(
            sourceBundleCacheKey,
            intentHash,
            graphToSave.SourceBundleHash,
            approvedResearchIntent,
            topicTitle,
            approvedMainTopic,
            approvedFocusArea,
            compressedContext);
        var quality = await EvaluateQualityAsync(userId, topicId, planRequestId, graphToSave, ct);
        await AlignSourcesAsync(userId, topicId, graphToSave, ct);

        return new ConceptGraphBuildResultDto
        {
            Graph = graphToSave,
            SnapshotId = snapshot.Id,
            CacheKey = cacheKey,
            SourceBundleCacheKey = sourceBundleCacheKey,
            QualityRunId = quality?.Id,
            QualityStatus = quality?.QualityStatus ?? "unknown",
            QualityCacheKey = $"orka:v10:graph-quality:{snapshot.Id:N}",
            CacheHit = cachedGraph != null
        };
    }

    public static string ComputeIntentHash(params string?[] parts)
    {
        var normalized = NormalizeKey(string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    public static string ComputeSourceBundleHash(CompressedPlanResearchContextDto context)
    {
        var payload = JsonSerializer.Serialize(new
        {
            context.GroundingMode,
            context.SourceCount,
            Sources = context.TopSources.Select(s => new { s.Provider, s.Title, s.Url }),
            context.CurriculumMapHints,
            context.PrerequisiteHints,
            context.LikelyMisconceptions
        }, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private async Task TryCacheSourceBundleAsync(
        string cacheKey,
        string intentHash,
        string sourceBundleHash,
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        CompressedPlanResearchContextDto context)
    {
        if (_redis == null)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                Version = 2,
                IntentHash = intentHash,
                SourceBundleHash = sourceBundleHash,
                ApprovedResearchIntent = approvedResearchIntent,
                TopicTitle = topicTitle,
                ApprovedMainTopic = approvedMainTopic,
                ApprovedFocusArea = approvedFocusArea,
                Context = context
            }, JsonOptions);
            await _redis.SetJsonAsync(cacheKey, payload, TimeSpan.FromHours(12));
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[ConceptGraph] Source bundle Redis write skipped. KeyRef={KeyRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeTextRef(cacheKey, "cache"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private async Task<ConceptGraphQualityDto?> EvaluateQualityAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        ConceptGraphDto graph,
        CancellationToken ct)
    {
        if (_quality == null) return null;
        try
        {
            return await _quality.EvaluateAndSaveAsync(userId, topicId, planRequestId, graph, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[ConceptGraph] Quality evaluation skipped. SnapshotRef={SnapshotRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(graph.SnapshotId, "snapshot"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private async Task AlignSourcesAsync(Guid userId, Guid? topicId, ConceptGraphDto graph, CancellationToken ct)
    {
        if (_alignment == null) return;
        try
        {
            await _alignment.AlignGraphSourcesAsync(userId, topicId, graph, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[ConceptGraph] Source alignment skipped. SnapshotRef={SnapshotRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(graph.SnapshotId, "snapshot"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private ConceptGraphDto BuildGraph(
        string intentHash,
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        CompressedPlanResearchContextDto context)
    {
        var scope = _scopePlanner.BuildScope(
            approvedResearchIntent,
            topicTitle,
            approvedMainTopic,
            approvedFocusArea,
            context);
        var domain = scope.Domain;
        var sourceBundleHash = ComputeSourceBundleHash(context);
        var sourceConfidence = SourceConfidence(context);
        var conceptSeeds = scope.Seeds.Count > 0
            ? scope.Seeds
            : new ConceptScopePlanner()
                .BuildScope(approvedResearchIntent, topicTitle, approvedMainTopic, approvedFocusArea, context)
                .Seeds;

        var outcomes = conceptSeeds.Select((seed, index) => new LearningOutcomeDto
        {
            StableKey = $"{seed.StableKey}-outcome",
            Label = $"{seed.Label} outcome",
            Description = $"Learner can explain, apply, or diagnose {seed.Label} within {topicTitle}.",
            StandardUri = $"orka:outcome:{intentHash}:{seed.StableKey}",
            CognitiveLevel = index % 4 == 0 ? "remember" : index % 4 == 1 ? "understand" : index % 4 == 2 ? "apply" : "analyze"
        }).ToList();

        var concepts = conceptSeeds.Select((seed, index) =>
        {
            return new LearningConceptDto
            {
                StableKey = seed.StableKey,
                Label = seed.Label,
                Description = BuildDescription(seed.Label, domain, topicTitle, seed.EvidenceBasis),
                DifficultyBand = string.IsNullOrWhiteSpace(seed.DifficultyBand) ? "core" : seed.DifficultyBand,
                Order = index,
                PrerequisiteKeys = seed.PrerequisiteKeys,
                Misconceptions = seed.Misconceptions.Count > 0 ? seed.Misconceptions : ["common misconception"],
                LearningOutcomeKeys = [outcomes[index].StableKey],
                SourceEvidenceLabels = seed.SourceEvidenceLabels
            };
        }).ToList();

        var relations = concepts
            .Skip(1)
            .Select(c => new ConceptRelationDto
            {
                SourceConceptKey = c.PrerequisiteKeys.FirstOrDefault() ?? concepts[0].StableKey,
                TargetConceptKey = c.StableKey,
                RelationType = "prerequisite",
                Weight = 1.0
            })
            .ToList();

        return new ConceptGraphDto
        {
            IntentHash = intentHash,
            TopicTitle = Trim(topicTitle, 180),
            ApprovedResearchIntent = Trim(approvedResearchIntent, 260),
            Domain = domain,
            SourceConfidence = sourceConfidence,
            SourceBundleHash = sourceBundleHash,
            Outcomes = outcomes,
            Concepts = concepts,
            Relations = relations,
            SourceEvidence = context.TopSources.Take(6).ToList(),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<ConceptGraphSnapshot> SaveSnapshotAsync(
        Guid userId,
        Guid? topicId,
        Guid planRequestId,
        ConceptGraphDto graph,
        CancellationToken ct)
    {
        var snapshot = new ConceptGraphSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            PlanRequestId = planRequestId,
            IntentHash = graph.IntentHash,
            ApprovedResearchIntent = graph.ApprovedResearchIntent,
            TopicTitle = graph.TopicTitle,
            Domain = graph.Domain,
            SourceConfidence = graph.SourceConfidence,
            SourceBundleHash = graph.SourceBundleHash,
            GraphJson = JsonSerializer.Serialize(graph, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        _db.ConceptGraphSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        var conceptEntities = graph.Concepts.Select(c => new LearningConcept
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshot.Id,
            StableKey = c.StableKey,
            Label = c.Label,
            Description = c.Description,
            DifficultyBand = c.DifficultyBand,
            Order = c.Order,
            PrerequisitesJson = JsonSerializer.Serialize(c.PrerequisiteKeys, JsonOptions),
            MisconceptionsJson = JsonSerializer.Serialize(c.Misconceptions, JsonOptions),
            LearningOutcomeKeysJson = JsonSerializer.Serialize(c.LearningOutcomeKeys, JsonOptions),
            SourceEvidenceJson = JsonSerializer.Serialize(c.SourceEvidenceLabels, JsonOptions),
            CreatedAt = DateTime.UtcNow
        }).ToList();
        _db.LearningConcepts.AddRange(conceptEntities);

        var conceptsByKey = conceptEntities.ToDictionary(c => c.StableKey, StringComparer.OrdinalIgnoreCase);
        foreach (var concept in graph.Concepts)
        {
            if (conceptsByKey.TryGetValue(concept.StableKey, out var entity))
            {
                concept.Id = entity.Id;
            }
        }

        var outcomeEntities = graph.Outcomes.Select(o => new LearningOutcome
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshot.Id,
            StableKey = o.StableKey,
            Label = o.Label,
            Description = o.Description,
            StandardUri = o.StandardUri,
            CognitiveLevel = o.CognitiveLevel,
            CreatedAt = DateTime.UtcNow
        }).ToList();
        _db.LearningOutcomes.AddRange(outcomeEntities);
        var outcomesByKey = outcomeEntities.ToDictionary(o => o.StableKey, StringComparer.OrdinalIgnoreCase);
        foreach (var outcome in graph.Outcomes)
        {
            if (outcomesByKey.TryGetValue(outcome.StableKey, out var entity))
            {
                outcome.Id = entity.Id;
            }
        }

        _db.ConceptRelations.AddRange(graph.Relations.Select(r => new ConceptRelation
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = snapshot.Id,
            SourceConceptKey = r.SourceConceptKey,
            TargetConceptKey = r.TargetConceptKey,
            RelationType = r.RelationType,
            Weight = r.Weight,
            CreatedAt = DateTime.UtcNow
        }));

        _db.OutcomeAlignments.AddRange(graph.Concepts.SelectMany(concept =>
            concept.LearningOutcomeKeys.Select(outcomeKey => new OutcomeAlignment
            {
                Id = Guid.NewGuid(),
                ConceptGraphSnapshotId = snapshot.Id,
                LearningOutcomeId = outcomesByKey.TryGetValue(outcomeKey, out var outcome) ? outcome.Id : null,
                EntityType = "concept",
                EntityId = conceptsByKey.TryGetValue(concept.StableKey, out var entity) ? entity.Id : null,
                EntityKey = concept.StableKey,
                AlignmentType = "addresses",
                Weight = 1.0,
                CreatedAt = DateTime.UtcNow
            })));

        snapshot.GraphJson = JsonSerializer.Serialize(graph, JsonOptions);
        await _db.SaveChangesAsync(ct);
        return snapshot;
    }

    private static ConceptGraphDto? DeserializeGraph(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ConceptGraphDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static ConceptGraphDto ToDto(ConceptGraphSnapshot snapshot) => new()
    {
        SnapshotId = snapshot.Id,
        IntentHash = snapshot.IntentHash,
        TopicTitle = snapshot.TopicTitle,
        ApprovedResearchIntent = snapshot.ApprovedResearchIntent,
        Domain = snapshot.Domain,
        SourceConfidence = snapshot.SourceConfidence,
        SourceBundleHash = snapshot.SourceBundleHash,
        Concepts = snapshot.Concepts
            .OrderBy(c => c.Order)
            .Select(c => new LearningConceptDto
            {
                Id = c.Id,
                StableKey = c.StableKey,
                Label = c.Label,
                Description = c.Description,
                DifficultyBand = c.DifficultyBand,
                Order = c.Order,
                PrerequisiteKeys = DeserializeList(c.PrerequisitesJson),
                Misconceptions = DeserializeList(c.MisconceptionsJson),
                LearningOutcomeKeys = DeserializeList(c.LearningOutcomeKeysJson),
                SourceEvidenceLabels = DeserializeList(c.SourceEvidenceJson)
            }).ToList(),
        Relations = snapshot.Relations.Select(r => new ConceptRelationDto
        {
            Id = r.Id,
            SourceConceptKey = r.SourceConceptKey,
            TargetConceptKey = r.TargetConceptKey,
            RelationType = r.RelationType,
            Weight = r.Weight
        }).ToList(),
        Outcomes = snapshot.Outcomes.Select(o => new LearningOutcomeDto
        {
            Id = o.Id,
            StableKey = o.StableKey,
            Label = o.Label,
            Description = o.Description,
            StandardUri = o.StandardUri,
            CognitiveLevel = o.CognitiveLevel
        }).ToList()
    };

    private static List<string> BuildConceptLabels(
        string approvedResearchIntent,
        string topicTitle,
        string approvedMainTopic,
        string approvedFocusArea,
        string domain,
        CompressedPlanResearchContextDto context)
        => ResearchConceptExtractor.ExtractConceptLabels(
            context,
            domain,
            approvedMainTopic,
            approvedFocusArea,
            approvedResearchIntent,
            topicTitle);

    private static string SourceConfidence(CompressedPlanResearchContextDto context)
    {
        if (context.GroundingMode == GroundingMode.SourceGrounded && context.SourceCount >= 3) return "high";
        if (context.GroundingMode is GroundingMode.SourceGrounded or GroundingMode.PartialSourceGrounded && context.SourceCount > 0) return "medium";
        return "low";
    }

    private static string BuildDescription(string label, string domain, string topicTitle, string evidenceBasis = "") =>
        $"{label} is a measurable {domain} learning concept inside {topicTitle}. It can be assessed, remediated, and connected to source evidence. Basis: {FirstNonBlank(evidenceBasis, "concept_scope_planner")}.";

    private static string ConceptLabelFromText(string text)
    {
        var clean = Regex.Replace(text, @"https?://\S+", " ");
        clean = Regex.Replace(clean, @"\b(www\.|doi:|isbn:)\S+", " ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"^[\-\*\d\.\)\s]+", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', '.', ':', ';');
        if (clean.Contains(':'))
        {
            clean = clean.Split(':', 2)[0].Trim();
        }
        return Trim(clean, 90);
    }

    private static bool LooksLikeResearchInstruction(string label)
    {
        var text = NormalizeSearch(label);
        return ContainsAny(text,
            "research", "search for", "look up", "find sources", "source-backed", "source grounded",
            "use sources", "provide citations", "provider", "tool", "workflow", "korteks",
            "compress", "summarize", "report", "brief", "learning path", "study plan",
            "map the focus", "identify sub-concepts", "break down", "start from", "move from",
            "watch for", "available videos", "youtube", "playlist");
    }

    private static bool LooksLikeSourceReference(string label)
    {
        var text = NormalizeSearch(label);
        return ContainsAny(text,
            "http", "www", ".com", ".org", ".edu", ".gov", "wikipedia", "khan academy",
            "coursera", "edx", "youtube", "video", "channel", "playlist", "official docs",
            "documentation", "article", "paper", "chapter", "book", "lecture notes",
            "retrieved", "source", "url");
    }

    private static bool LooksLikeCurriculumContainer(string label)
    {
        var text = NormalizeSearch(label);
        return ContainsAny(text,
            "unit ", "module ", "lesson ", "week ", "day ", "part ", "phase ",
            "checkpoint", "roadmap", "syllabus", "curriculum", "course outline",
            "introductory", "advanced topics", "practice set", "homework", "exercise set",
            "exam prep", "mock test", "review session");
    }

    private static bool IsUsefulLabel(string label) =>
        !string.IsNullOrWhiteSpace(label) &&
        label.Length >= 4 &&
        !ContainsAny(NormalizeSearch(label),
            "start from prerequisites",
            "move from small examples",
            "small examp",
            "map the focus area",
            "sub-concepts",
            "sub concepts",
            "prior skills",
            "basic examples",
            "learner starts",
            "watch for",
            "memorized definitions",
            "confused terminology",
            "direct learning",
            "research brief",
            "need source grounded",
            "practiceorder",
            "learning path",
            "conservative curriculum",
            "provider-backed sources",
            "available videos",
            "generated from",
            "research intent") &&
        !label.Contains("degraded", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeKey(string value)
    {
        var normalized = NormalizeSearch(value);
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "concept" : normalized;
    }

    private static string NormalizeSearch(string value) =>
        value.ToLowerInvariant().Transliterate();

    private static string NormalizeSearchLegacy(string value) =>
        value.ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u')
            .Replace('\u00c3', 'a')
            .Replace('\u00c4', 'a')
            .Replace('\u00c5', 's');

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = Regex.Replace(value.Trim(), @"\s+", " ");
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }
}
