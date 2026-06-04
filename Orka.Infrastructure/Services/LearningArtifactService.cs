using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class LearningArtifactService : ILearningArtifactService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ArtifactTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "concept_summary", "worked_example", "misconception_repair_card", "retrieval_card",
        "prerequisite_bridge", "diagram", "mermaid_diagram", "image_reference", "video_reference",
        "table", "formula", "code_trace", "code_explanation", "source_excerpt_summary",
        "wiki_study_note", "plan_step_note", "quiz_remediation_note", "next_action_card",
        "study_guide", "briefing_doc", "milestone_review", "source_digest", "mind_map",
        "audio_overview", "audio_script", "slide_deck_outline", "flashcard_set",
        "review_quiz", "misconception_repair_pack", "worked_example_set", "retrieval_card_set",
        "audio_transcript", "caption_track", "video_ready_package", "slide_export_manifest",
        "narration_script", "visual_instruction_set", "media_accessibility_note",
        "source_question_thread", "source_question_review_summary",
        "glossary", "timeline", "uml_diagram", "properties_panel", "tag_map",
        "backlink_map", "linked_mentions", "reference_map", "graph_view",
        "template_set", "search_filter_index"
    };

    private static readonly HashSet<string> Origins = new(StringComparer.OrdinalIgnoreCase)
    {
        "tutor", "wiki", "plan", "quiz", "tool", "source", "code", "manual", "notebook"
    };

    private static readonly HashSet<string> RenderFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "markdown", "mermaid", "json_table", "formula_text", "code_text", "media_reference", "plain_text"
    };

    private static readonly HashSet<string> SourceBases = new(StringComparer.OrdinalIgnoreCase)
    {
        "source_grounded", "wiki_backed", "tool_evidence", "code_output", "model_assisted", "evidence_insufficient"
    };

    private readonly OrkaDbContext _db;

    public LearningArtifactService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<LearningArtifactDto> CreateArtifactAsync(Guid userId, LearningArtifactRequestDto request, CancellationToken ct = default)
    {
        var safety = await ValidateArtifactAsync(userId, request, ct);
        if (safety.BlockingIssues.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", safety.BlockingIssues));
        }

        var now = DateTime.UtcNow;
        var accessibility = BuildAccessibility(request);
        var entity = new LearningArtifact
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            TutorTurnStateId = request.TutorTurnStateId,
            TutorActionTraceId = request.TutorActionTraceId,
            TeachingArtifactId = request.TeachingArtifactId,
            ActiveLessonSnapshotId = request.ActiveLessonSnapshotId,
            StudentContextSnapshotId = request.StudentContextSnapshotId,
            PlanQualitySnapshotId = request.PlanQualitySnapshotId,
            AssessmentQualitySnapshotId = request.AssessmentQualitySnapshotId,
            SourceEvidenceBundleId = request.SourceEvidenceBundleId,
            WikiNotebookSectionKey = TrimOrNull(request.WikiNotebookSectionKey),
            ConceptKey = TrimOrNull(request.ConceptKey),
            ConceptLabel = TrimOrNull(request.ConceptLabel),
            ArtifactType = NormalizeArtifactType(request.ArtifactType),
            ArtifactStatus = NormalizeStatus(request.ArtifactStatus, safety),
            Origin = NormalizeOrigin(request.Origin),
            RenderFormat = NormalizeRenderFormat(request.RenderFormat, request.ArtifactType),
            Title = Trim(request.Title, 180),
            SafeContent = ClipSafeContent(request.SafeContent),
            ContentJson = ClipNullable(request.ContentJson, 6000),
            SourceBasis = NormalizeSourceBasis(request.SourceBasis),
            CitationIdsJson = Serialize(request.CitationIds.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray()),
            ToolTraceIdsJson = Serialize(request.ToolTraceIds.Distinct().Take(20).ToArray()),
            AccessibilityJson = Serialize(accessibility),
            SafetyWarningsJson = Serialize(safety.Warnings.Concat(accessibility.Issues).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray()),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.LearningArtifacts.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity, safety, accessibility);
    }

    public async Task<LearningArtifactDto?> GetArtifactAsync(Guid userId, Guid artifactId, CancellationToken ct = default)
    {
        var entity = await _db.LearningArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == artifactId && a.UserId == userId && !a.IsDeleted, ct);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<LearningArtifactListDto> ListArtifactsAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, string? conceptKey = null, CancellationToken ct = default)
    {
        var query = _db.LearningArtifacts.AsNoTracking().Where(a => a.UserId == userId && !a.IsDeleted);
        if (topicId.HasValue) query = query.Where(a => a.TopicId == topicId);
        if (sessionId.HasValue) query = query.Where(a => a.SessionId == sessionId);
        if (!string.IsNullOrWhiteSpace(conceptKey)) query = query.Where(a => a.ConceptKey == conceptKey);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return new LearningArtifactListDto
        {
            Count = items.Count,
            Items = items.Select(item => ToDto(item)).ToArray()
        };
    }

    public async Task<LearningArtifactDto?> RefreshArtifactStatusAsync(Guid userId, Guid artifactId, string? reason = null, CancellationToken ct = default)
    {
        var entity = await _db.LearningArtifacts
            .FirstOrDefaultAsync(a => a.Id == artifactId && a.UserId == userId && !a.IsDeleted, ct);
        if (entity == null) return null;

        var warnings = ParseStringArray(entity.SafetyWarningsJson).ToList();
        if (entity.SourceBasis == "source_grounded")
        {
            var hasReadyBundle = entity.SourceEvidenceBundleId.HasValue &&
                                 await _db.SourceEvidenceBundles.AsNoTracking().AnyAsync(b =>
                                     b.Id == entity.SourceEvidenceBundleId &&
                                     b.UserId == userId &&
                                     !b.IsDeleted &&
                                     (b.EvidenceStatus == "source_grounded" || b.EvidenceStatus == "mixed"), ct);
            if (!hasReadyBundle)
            {
                entity.ArtifactStatus = "stale";
                warnings.Add("source_evidence_stale_or_missing");
            }
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            warnings.Add($"refresh:{Trim(reason, 80)}");
        }

        entity.SafetyWarningsJson = Serialize(warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray());
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<LearningArtifactSafetyDto> ValidateArtifactAsync(Guid userId, LearningArtifactRequestDto request, CancellationToken ct = default)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();
        var artifactType = NormalizeArtifactType(request.ArtifactType);
        var origin = NormalizeOrigin(request.Origin);
        var renderFormat = NormalizeRenderFormat(request.RenderFormat, artifactType);
        var sourceBasis = NormalizeSourceBasis(request.SourceBasis);
        var content = request.SafeContent ?? string.Empty;

        if (!ArtifactTypes.Contains(artifactType)) blocking.Add("unsupported_artifact_type");
        if (!Origins.Contains(origin)) blocking.Add("unsupported_artifact_origin");
        if (!RenderFormats.Contains(renderFormat)) blocking.Add("unsupported_render_format");
        if (!SourceBases.Contains(sourceBasis)) blocking.Add("unsupported_source_basis");
        if (string.IsNullOrWhiteSpace(request.Title)) blocking.Add("missing_title");
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(request.ContentJson)) blocking.Add("missing_safe_content");

        if (LooksUnsafe(content) || LooksUnsafe(request.ContentJson))
            blocking.Add("unsafe_raw_or_executable_content");
        if (ContainsLocalPath(content) || ContainsLocalPath(request.ContentJson))
            blocking.Add("local_path_reference_blocked");
        if (ContainsUnsafeCopy(content))
            blocking.Add("unsafe_learning_claim_or_workflow_copy");
        if (content.Length > 12000)
            warnings.Add("content_clipped_for_safe_rendering");

        if (sourceBasis == "source_grounded")
        {
            var hasCitation = request.CitationIds.Any(c => !string.IsNullOrWhiteSpace(c));
            var hasReadyBundle = request.SourceEvidenceBundleId.HasValue &&
                                 await _db.SourceEvidenceBundles.AsNoTracking().AnyAsync(b =>
                                     b.Id == request.SourceEvidenceBundleId &&
                                     b.UserId == userId &&
                                     !b.IsDeleted &&
                                     (b.EvidenceStatus == "source_grounded" || b.EvidenceStatus == "mixed"), ct);
            if (!hasCitation && !hasReadyBundle)
                blocking.Add("source_grounded_artifact_requires_active_evidence");
        }

        if (artifactType is "video_reference" or "image_reference" && sourceBasis == "source_grounded" && request.SourceEvidenceBundleId == null && request.CitationIds.Count == 0)
            blocking.Add("media_cannot_ground_factual_claim_without_verified_evidence");
        if (artifactType is "video_reference" && sourceBasis != "source_grounded")
            warnings.Add("media_reference_is_pedagogy_only");
        if (artifactType is "image_reference" or "video_reference" && string.IsNullOrWhiteSpace(request.Accessibility.AltText) && string.IsNullOrWhiteSpace(request.Accessibility.Caption))
            warnings.Add("media_missing_alt_or_caption");
        if (artifactType is "diagram" or "mermaid_diagram" && string.IsNullOrWhiteSpace(request.Accessibility.Caption) && string.IsNullOrWhiteSpace(request.Accessibility.Summary))
            warnings.Add("diagram_missing_caption_or_summary");
        if (artifactType == "formula" && string.IsNullOrWhiteSpace(request.Accessibility.TextFallback) && string.IsNullOrWhiteSpace(content))
            blocking.Add("formula_requires_text_fallback");
        if (artifactType == "table" && string.IsNullOrWhiteSpace(request.Accessibility.Caption) && string.IsNullOrWhiteSpace(request.Accessibility.Summary))
            warnings.Add("table_missing_caption_or_summary");
        if (artifactType is "code_trace" or "code_explanation" && (string.IsNullOrWhiteSpace(request.Accessibility.Language) || string.IsNullOrWhiteSpace(request.Accessibility.Summary)))
            warnings.Add("code_artifact_missing_language_or_status_summary");
        if (renderFormat == "mermaid" && (content.Length > 6000 || LooksUnsafeMermaid(content)))
            blocking.Add("mermaid_artifact_not_bounded_or_safe");

        return new LearningArtifactSafetyDto
        {
            Status = blocking.Count > 0 ? "blocked" : warnings.Count > 0 ? "warning" : "safe",
            BlockingIssues = blocking.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public async Task<LearningArtifactDto> MirrorTeachingArtifactAsync(
        Guid userId,
        TeachingArtifact artifact,
        TutorTurnStateDto? turnState = null,
        TutorActionPlanDto? actionPlan = null,
        string origin = "tutor",
        CancellationToken ct = default)
    {
        var existing = await _db.LearningArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.TeachingArtifactId == artifact.Id && !a.IsDeleted, ct);
        if (existing != null) return ToDto(existing);

        var type = NormalizeArtifactType(artifact.ArtifactType);
        var request = new LearningArtifactRequestDto
        {
            TopicId = artifact.TopicId,
            SessionId = artifact.SessionId,
            TutorTurnStateId = turnState?.Id,
            TutorActionTraceId = artifact.TutorActionTraceId ?? actionPlan?.Id,
            TeachingArtifactId = artifact.Id,
            ActiveLessonSnapshotId = turnState?.ActiveLessonSnapshotId,
            StudentContextSnapshotId = turnState?.StudentContextSnapshotId,
            PlanQualitySnapshotId = turnState?.PlanQualitySnapshotId,
            ConceptKey = turnState?.ActiveConceptKey,
            ConceptLabel = turnState?.ActiveConceptLabel,
            ArtifactType = type,
            ArtifactStatus = artifact.Status is "ready" or "rendered" ? "ready" : "degraded",
            Origin = origin,
            RenderFormat = NormalizeRenderFormat(artifact.RenderFormat, type),
            Title = artifact.Title,
            SafeContent = artifact.Content,
            SourceBasis = SourceBasisFor(artifact, turnState, origin),
            ToolTraceIds = Array.Empty<Guid>(),
            Accessibility = AccessibilityFor(artifact, type)
        };

        return await CreateArtifactAsync(userId, request, ct);
    }

    public async Task<IReadOnlyList<LearningArtifactDto>> BuildArtifactForTutorActionAsync(
        Guid userId,
        TutorTurnStateDto turnState,
        TutorActionPlanDto actionPlan,
        TutorResponsePolicyDto? policy = null,
        CancellationToken ct = default)
    {
        var artifacts = new List<LearningArtifactDto>();
        foreach (var plan in actionPlan.ArtifactPlans.Take(8))
        {
            var type = NormalizeArtifactType(plan.ArtifactType);
            if (!ArtifactTypes.Contains(type)) continue;
            var title = string.IsNullOrWhiteSpace(turnState.ActiveConceptLabel)
                ? type.Replace('_', ' ')
                : $"{turnState.ActiveConceptLabel} - {type.Replace('_', ' ')}";
            var request = new LearningArtifactRequestDto
            {
                TopicId = turnState.TopicId,
                SessionId = turnState.SessionId,
                TutorTurnStateId = turnState.Id,
                TutorActionTraceId = actionPlan.Id,
                ActiveLessonSnapshotId = turnState.ActiveLessonSnapshotId,
                StudentContextSnapshotId = turnState.StudentContextSnapshotId,
                PlanQualitySnapshotId = turnState.PlanQualitySnapshotId,
                ConceptKey = turnState.ActiveConceptKey,
                ConceptLabel = turnState.ActiveConceptLabel,
                ArtifactType = type,
                ArtifactStatus = "draft",
                Origin = "tutor",
                RenderFormat = NormalizeRenderFormat(plan.RenderFormat, type),
                Title = title,
                SafeContent = $"{title}\n\nAmac: {Trim(plan.Reason, 300)}",
                SourceBasis = policy?.SourceReadiness == "source_grounded" ? "source_grounded" : "model_assisted",
                SourceEvidenceBundleId = null,
                Accessibility = new LearningArtifactAccessibilityDto
                {
                    Status = "usable",
                    Caption = plan.Reason,
                    Summary = plan.Reason,
                    TextFallback = title
                }
            };
            var safety = await ValidateArtifactAsync(userId, request, ct);
            if (safety.BlockingIssues.Count == 0)
            {
                artifacts.Add(await CreateArtifactAsync(userId, request, ct));
            }
        }

        return artifacts;
    }

    public async Task<LearningArtifactDto> BuildArtifactForWikiSectionAsync(Guid userId, Guid topicId, string sectionKey, CancellationToken ct = default)
    {
        var notebook = await _db.WikiKnowledgeNotebookSnapshots
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.TopicId == topicId && !n.IsDeleted)
            .OrderByDescending(n => n.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var request = new LearningArtifactRequestDto
        {
            TopicId = topicId,
            WikiNotebookSectionKey = sectionKey,
            ArtifactType = "wiki_study_note",
            ArtifactStatus = notebook == null ? "degraded" : "ready",
            Origin = "wiki",
            RenderFormat = "markdown",
            Title = $"Wiki calisma notu: {Trim(sectionKey, 80)}",
            SafeContent = notebook == null
                ? "Bu Wiki bolumu icin henuz guclu kaynak/notebook kaniti yok."
                : $"Bu not Wiki bolumune bagli guvenli calisma notudur: `{Trim(sectionKey, 80)}`.",
            SourceBasis = notebook == null ? "evidence_insufficient" : "wiki_backed",
            Accessibility = new LearningArtifactAccessibilityDto
            {
                Status = "usable",
                Summary = "Wiki calisma notu",
                TextFallback = "Wiki calisma notu"
            }
        };
        return await CreateArtifactAsync(userId, request, ct);
    }

    public async Task<LearningArtifactDto> BuildArtifactForQuizRemediationAsync(Guid userId, LearningArtifactRequestDto request, CancellationToken ct = default)
    {
        request.ArtifactType = NormalizeArtifactType(string.IsNullOrWhiteSpace(request.ArtifactType) ? "quiz_remediation_note" : request.ArtifactType);
        request.Origin = "quiz";
        request.ArtifactStatus = string.IsNullOrWhiteSpace(request.ArtifactStatus) ? "ready" : request.ArtifactStatus;
        request.SourceBasis = string.IsNullOrWhiteSpace(request.SourceBasis) ? "model_assisted" : request.SourceBasis;
        return await CreateArtifactAsync(userId, request, ct);
    }

    private static LearningArtifactDto ToDto(LearningArtifact entity, LearningArtifactSafetyDto? safety = null, LearningArtifactAccessibilityDto? accessibility = null) => new()
    {
        Id = entity.Id,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        TutorTurnStateId = entity.TutorTurnStateId,
        TutorActionTraceId = entity.TutorActionTraceId,
        TeachingArtifactId = entity.TeachingArtifactId,
        ActiveLessonSnapshotId = entity.ActiveLessonSnapshotId,
        StudentContextSnapshotId = entity.StudentContextSnapshotId,
        PlanQualitySnapshotId = entity.PlanQualitySnapshotId,
        AssessmentQualitySnapshotId = entity.AssessmentQualitySnapshotId,
        SourceEvidenceBundleId = entity.SourceEvidenceBundleId,
        WikiNotebookSectionKey = entity.WikiNotebookSectionKey,
        ConceptKey = entity.ConceptKey,
        ConceptLabel = entity.ConceptLabel,
        ArtifactType = entity.ArtifactType,
        ArtifactStatus = entity.ArtifactStatus,
        Origin = entity.Origin,
        RenderFormat = entity.RenderFormat,
        Title = entity.Title,
        SafeContent = entity.SafeContent,
        ContentJson = entity.ContentJson,
        SourceBasis = entity.SourceBasis,
        CitationIds = ParseStringArray(entity.CitationIdsJson),
        ToolTraceIds = ParseGuidArray(entity.ToolTraceIdsJson),
        PhaseScope = ExtractPhaseScope(entity.ContentJson),
        Accessibility = accessibility ?? ParseAccessibility(entity.AccessibilityJson),
        Safety = safety ?? new LearningArtifactSafetyDto
        {
            Status = ParseStringArray(entity.SafetyWarningsJson).Count == 0 ? "safe" : "warning",
            Warnings = ParseStringArray(entity.SafetyWarningsJson)
        },
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    private static string NormalizeArtifactType(string? value)
    {
        var key = Normalize(value);
        return key switch
        {
            "mermaid_graph" => "mermaid_diagram",
            "comparison_table" => "table",
            "timeline" => "timeline",
            "image_prompt" => "image_reference",
            "micro_quiz" => "retrieval_card",
            "evidence_card" => "source_excerpt_summary",
            "forum_pattern" => "misconception_repair_card",
            "map_context" => "diagram",
            "science_fact_card" => "source_excerpt_summary",
            "research_reading_card" => "source_excerpt_summary",
            "real_world_graph" => "table",
            "code_lab_task" => "code_explanation",
            "notebook_study_guide" => "study_guide",
            "notebook_mind_map" => "mind_map",
            "notebook_audio" => "audio_overview",
            "transcript" => "audio_transcript",
            "captions" => "caption_track",
            "video_outline" => "video_ready_package",
            "slide_manifest" => "slide_export_manifest",
            "uml" => "uml_diagram",
            "mermaid_uml" => "uml_diagram",
            "properties" => "properties_panel",
            "property_panel" => "properties_panel",
            "metadata_panel" => "properties_panel",
            "tags" => "tag_map",
            "backlinks" => "backlink_map",
            "linked_mention" => "linked_mentions",
            "linked_mentions_map" => "linked_mentions",
            "block_refs" => "reference_map",
            "block_references" => "reference_map",
            "references" => "reference_map",
            "graph" => "graph_view",
            "templates" => "template_set",
            "template" => "template_set",
            "search_filter" => "search_filter_index",
            "search_filters" => "search_filter_index",
            "worked_example" => "worked_example",
            "retrieval_card" => "retrieval_card",
            "" => "concept_summary",
            _ => key
        };
    }

    private static string NormalizeStatus(string? value, LearningArtifactSafetyDto safety)
    {
        if (safety.BlockingIssues.Count > 0) return "rejected";
        var key = Normalize(value);
        return key is "draft" or "ready" or "degraded" or "stale" or "rejected" or "archived" ? key : "draft";
    }

    private static string NormalizeOrigin(string? value)
    {
        var key = Normalize(value);
        return Origins.Contains(key) ? key : "manual";
    }

    private static string NormalizeRenderFormat(string? value, string artifactType)
    {
        var key = Normalize(value);
        if (key == "image") return "media_reference";
        if (key == "table") return "json_table";
        if (key == "formula") return "formula_text";
        if (key == "code") return "code_text";
        if (key == "mermaid" || artifactType == "mermaid_diagram") return "mermaid";
        return RenderFormats.Contains(key) ? key : "markdown";
    }

    private static string NormalizeSourceBasis(string? value)
    {
        var key = Normalize(value);
        return SourceBases.Contains(key) ? key : "evidence_insufficient";
    }

    private static string SourceBasisFor(TeachingArtifact artifact, TutorTurnStateDto? turn, string origin)
    {
        var type = NormalizeArtifactType(artifact.ArtifactType);
        if (origin == "wiki") return "wiki_backed";
        if (type is "code_trace" or "code_explanation") return "code_output";
        if (artifact.Provider?.Contains("tool", StringComparison.OrdinalIgnoreCase) == true) return "tool_evidence";
        if (type is "source_excerpt_summary" or "retrieval_card") return turn?.SourceEvidenceCount > 0 ? "model_assisted" : "evidence_insufficient";
        if (type is "image_reference" or "video_reference") return "model_assisted";
        return "model_assisted";
    }

    private static LearningArtifactAccessibilityDto AccessibilityFor(TeachingArtifact artifact, string type)
    {
        var title = string.IsNullOrWhiteSpace(artifact.Title) ? artifact.ArtifactType : artifact.Title;
        return new LearningArtifactAccessibilityDto
        {
            Status = "usable",
            AltText = type is "image_reference" or "diagram" or "mermaid_diagram" ? title : null,
            Caption = title,
            Summary = title,
            TextFallback = ClipSafeContent(artifact.Content),
            Language = type is "code_trace" or "code_explanation" ? "text" : null
        };
    }

    private static LearningArtifactAccessibilityDto BuildAccessibility(LearningArtifactRequestDto request)
    {
        var dto = request.Accessibility ?? new LearningArtifactAccessibilityDto();
        var issues = new List<string>(dto.Issues ?? Array.Empty<string>());
        var type = NormalizeArtifactType(request.ArtifactType);
        if (type is "image_reference" or "video_reference" && string.IsNullOrWhiteSpace(dto.AltText) && string.IsNullOrWhiteSpace(dto.Caption))
            issues.Add("missing_alt_or_caption");
        if (type is "diagram" or "mermaid_diagram" && string.IsNullOrWhiteSpace(dto.Caption) && string.IsNullOrWhiteSpace(dto.Summary))
            issues.Add("missing_diagram_caption_or_summary");
        if (type == "formula" && string.IsNullOrWhiteSpace(dto.TextFallback) && string.IsNullOrWhiteSpace(request.SafeContent))
            issues.Add("missing_formula_text_fallback");

        dto.Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        dto.Status = issues.Count == 0 ? "usable" : "needs_review";
        return dto;
    }

    private static bool LooksUnsafe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ContainsAny(text,
            "<script", "<iframe", "<object", "<embed", "<foreignObject", "javascript:",
            "onerror=", "onload=", "onclick=", "rawPrompt", "systemPrompt", "hiddenPrompt",
            "rawProviderPayload", "rawSourceChunk", "rawToolPayload", "debugTrace", "localPath",
            "apiKey", "secret=", "correctAnswer", "answerKey");
    }

    private static bool LooksUnsafeMermaid(string text) =>
        ContainsAny(text, "<script", "<iframe", "javascript:", "click ", "href ");

    private static bool ContainsLocalPath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return Regex.IsMatch(text, @"[A-Za-z]:\\|\\\\|/\.\./|\\\.\.|file://", RegexOptions.IgnoreCase);
    }

    private static bool ContainsUnsafeCopy(string text) =>
        ContainsAny(text,
            "kesin basarirsin", "kesin başarırsın", "garanti kazanirsin", "garanti kazanırsın",
            "resmi osym simulasyonu", "resmi ösym simülasyonu", "resmi meb simulasyonu",
            "mufredat tamam", "müfredat tamam", "ogretmen panel", "öğretmen panel",
            "dershane panel", "sinif yonetimi", "sınıf yönetimi");

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string ClipSafeContent(string? value) => Trim(value, 12000);
    private static string? ClipNullable(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Trim(value, max);
    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<Guid> ParseGuidArray(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? Array.Empty<Guid>() : JsonSerializer.Deserialize<Guid[]>(json, JsonOptions) ?? Array.Empty<Guid>(); }
        catch { return Array.Empty<Guid>(); }
    }

    private static IReadOnlyList<string> ExtractPhaseScope(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            var root = document.RootElement;
            if (TryGetPropertyIgnoreCase(root, "phaseScope", out var topLevel) && topLevel.ValueKind == JsonValueKind.Array)
            {
                var scope = ReadStringArray(topLevel);
                if (scope.Count > 0) return scope;
            }
            if (TryGetPropertyIgnoreCase(root, "featureContract", out var contract) &&
                contract.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(contract, "phaseScope", out var nested) &&
                nested.ValueKind == JsonValueKind.Array)
            {
                var scope = ReadStringArray(nested);
                if (scope.Count > 0) return scope;
            }
        }
        catch
        {
            // Fall through to the safe substring fallback below.
        }

        if (contentJson.Contains("phase_1_contract", StringComparison.OrdinalIgnoreCase) &&
            contentJson.Contains("phase_7_audio_classroom", StringComparison.OrdinalIgnoreCase))
        {
            return NotebookStudioPhaseScope.All;
        }

        return Array.Empty<string>();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element) =>
        element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static LearningArtifactAccessibilityDto ParseAccessibility(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? new LearningArtifactAccessibilityDto() : JsonSerializer.Deserialize<LearningArtifactAccessibilityDto>(json, JsonOptions) ?? new LearningArtifactAccessibilityDto(); }
        catch { return new LearningArtifactAccessibilityDto(); }
    }
}
