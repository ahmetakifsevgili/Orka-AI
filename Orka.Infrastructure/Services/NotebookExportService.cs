using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class NotebookExportService : INotebookExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "slide_preview", "markdown", "html", "manifest_only", "pptx_local_proof"
    };

    private readonly OrkaDbContext _db;
    private readonly ILearningRuntimeTelemetryService _telemetry;

    public NotebookExportService(OrkaDbContext db, ILearningRuntimeTelemetryService telemetry)
    {
        _db = db;
        _telemetry = telemetry;
    }

    public async Task<NotebookSlideExportPreviewDto?> BuildSlidePreviewAsync(
        Guid userId,
        Guid packId,
        CancellationToken ct = default)
    {
        var context = await LoadContextAsync(userId, packId, null, ct);
        if (context == null) return null;

        var preview = BuildPreview(context.Value.Pack, context.Value.SlideArtifact);
        await RecordTelemetryAsync(userId, context.Value.Pack, "export_preview_requested", preview.ExportReadiness, preview.Warnings.Count, ct);
        return preview;
    }

    public async Task<NotebookExportResultDto?> ExportAsync(
        Guid userId,
        Guid packId,
        NotebookExportRequestDto request,
        CancellationToken ct = default)
    {
        var format = NormalizeFormat(request.Format);
        var context = await LoadContextAsync(userId, packId, request.SlideDeckArtifactId, ct);
        if (context == null) return null;

        var pack = context.Value.Pack;
        var slideArtifact = context.Value.SlideArtifact;
        var manifestArtifact = context.Value.ManifestArtifact;
        var preview = BuildPreview(pack, slideArtifact);
        var warnings = preview.Warnings.ToList();
        var status = preview.SlideCount == 0 ? "degraded" : "ready";
        var exportReadiness = preview.SlideCount == 0 ? "manifest_ready" : "preview_ready";
        var contentType = "text/markdown";
        var content = string.Empty;
        string? fileName = null;

        if (!SupportedFormats.Contains(format))
        {
            format = "manifest_only";
            status = "unsupported";
            exportReadiness = "unsupported";
            warnings.Add("Requested export format is unsupported; manifest-only fallback returned.");
        }

        switch (format)
        {
            case "slide_preview":
                content = BuildMarkdown(preview, includeNotes: false);
                fileName = $"{Slug(preview.DeckTitle)}-preview.md";
                break;
            case "markdown":
                content = BuildMarkdown(preview, includeNotes: true);
                fileName = $"{Slug(preview.DeckTitle)}-slides.md";
                break;
            case "html":
                content = BuildHtml(preview);
                contentType = "text/html";
                fileName = $"{Slug(preview.DeckTitle)}-slides.html";
                break;
            case "pptx_local_proof":
                status = "unsupported";
                exportReadiness = "pptx_not_enabled";
                content = "PPTX local proof is not enabled in this repo runtime. Use slide preview, markdown, HTML, or manifest-only export package.";
                fileName = null;
                warnings.Add("No local PPTX export dependency is installed; no binary export was generated.");
                break;
            default:
                content = BuildManifest(preview, manifestArtifact != null);
                fileName = $"{Slug(preview.DeckTitle)}-manifest.md";
                exportReadiness = manifestArtifact == null ? "manifest_ready" : "outline_ready";
                break;
        }

        var safety = ValidateExportContent(content);
        if (safety.BlockingIssues.Count > 0)
        {
            status = "failed";
            exportReadiness = "failed";
            content = "Export blocked because the package contained unsafe or internal-only terms.";
            fileName = null;
        }

        var result = new NotebookExportResultDto
        {
            PackId = pack.Id,
            SlideDeckArtifactId = slideArtifact?.Id,
            Surface = preview.Surface,
            ContextType = preview.ContextType,
            WikiPageId = preview.WikiPageId,
            SourceId = preview.SourceId,
            ExportScope = preview.ExportScope,
            SourceUploadAllowed = preview.SourceUploadAllowed,
            CrossSurfaceSync = false,
            Format = format,
            Status = status,
            ExportReadiness = exportReadiness,
            Title = preview.DeckTitle,
            SourceBasis = preview.SourceBasis,
            SourceReadiness = preview.SourceReadiness,
            Content = content,
            ContentType = contentType,
            FileName = fileName,
            BinaryExportAvailable = false,
            PptxLocalProofAvailable = false,
            Preview = preview,
            Safety = safety,
            Accessibility = BuildAccessibility(preview),
            TemplateKeys = preview.TemplateKeys,
            SearchFilterKeys = preview.SearchFilterKeys,
            InternalConnectionKeys = preview.InternalConnectionKeys,
            PhaseScope = NotebookStudioPhaseScope.All,
            Warnings = warnings.Concat(safety.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await RecordTelemetryAsync(userId, pack, status == "unsupported" ? "unsupported_export_requested" : status == "failed" ? "export_failed" : "export_created", status, result.Warnings.Count, ct);
        return result;
    }

    private async Task<ExportContext?> LoadContextAsync(Guid userId, Guid packId, Guid? slideDeckArtifactId, CancellationToken ct)
    {
        var pack = await _db.LearningNotebookPacks.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == packId && p.UserId == userId && !p.IsDeleted, ct);
        if (pack == null) return null;

        var artifactIds = ParseGuids(pack.ArtifactIdsJson);
        var artifacts = await _db.LearningArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .Where(a => artifactIds.Contains(a.Id) || (slideDeckArtifactId.HasValue && a.Id == slideDeckArtifactId.Value))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var slideArtifact = slideDeckArtifactId.HasValue
            ? artifacts.FirstOrDefault(a => a.Id == slideDeckArtifactId.Value && a.ArtifactType == "slide_deck_outline")
            : artifacts.FirstOrDefault(a => a.ArtifactType == "slide_deck_outline");
        var manifestArtifact = artifacts.FirstOrDefault(a => a.ArtifactType == "slide_export_manifest");
        return new ExportContext(pack, slideArtifact, manifestArtifact);
    }

    private static NotebookSlideExportPreviewDto BuildPreview(LearningNotebookPack pack, LearningArtifact? slideArtifact)
    {
        var slides = slideArtifact == null ? Array.Empty<NotebookSlideExportItemDto>() : ParseSlides(slideArtifact.ContentJson);
        var warnings = ParseStrings(pack.WarningsJson).ToList();
        var surfaceContext = ResolveSurfaceContext(pack);
        if (slideArtifact == null)
            warnings.Add("Slide deck outline artifact is missing; export can only return a manifest-style fallback.");
        if (pack.EvidenceStatus is "evidence_insufficient" or "degraded" or "stale")
            warnings.Add("Evidence is limited; exported deck must not be presented as source-grounded.");

        return new NotebookSlideExportPreviewDto
        {
            PackId = pack.Id,
            SlideDeckArtifactId = slideArtifact?.Id,
            Surface = surfaceContext.Surface,
            ContextType = surfaceContext.ContextType,
            WikiPageId = surfaceContext.WikiPageId,
            SourceId = surfaceContext.SourceId,
            ExportScope = surfaceContext.ExportScope,
            SourceUploadAllowed = surfaceContext.Surface == "orkalm",
            CrossSurfaceSync = false,
            DeckTitle = Clip(slideArtifact?.Title ?? pack.Title, 180),
            SlideCount = slides.Count,
            SourceBasis = slideArtifact?.SourceBasis ?? SourceBasisFor(pack.EvidenceStatus),
            SourceReadiness = pack.SourceReadiness,
            ExportReadiness = slides.Count == 0 ? "manifest_ready" : "preview_ready",
            Slides = slides,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
            TemplateKeys = TemplateKeysFor(surfaceContext.Surface),
            SearchFilterKeys = SearchFilterKeysFor(surfaceContext.Surface),
            InternalConnectionKeys = InternalConnectionKeysFor(surfaceContext.Surface),
            PhaseScope = NotebookStudioPhaseScope.All,
            AccessibilitySummary = slides.Count == 0
                ? "Text fallback is available, but slide-level accessibility metadata is missing until a slide outline exists."
                : "Slide preview includes titles, bullets, speaker notes, checkpoint questions, and text fallback.",
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<NotebookSlideExportItemDto> ParseSlides(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return Array.Empty<NotebookSlideExportItemDto>();
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (!document.RootElement.TryGetProperty("slides", out var slidesElement) || slidesElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<NotebookSlideExportItemDto>();

            var slides = new List<NotebookSlideExportItemDto>();
            foreach (var slide in slidesElement.EnumerateArray().Take(20))
            {
                var order = GetInt(slide, "order", slides.Count + 1);
                var title = Clip(GetString(slide, "title") ?? $"Slide {order}", 140);
                var bullets = GetStringArray(slide, "bullets").Select(b => Clip(b, 220)).Where(NotBlank).Take(8).ToArray();
                var speakerNotes = Clip(GetString(slide, "speakerNotes"), 700);
                var checkpoint = Clip(GetString(slide, "checkpointQuestion"), 260);
                var accessibility = Clip(GetString(slide, "accessibilitySummary") ?? "Text fallback available.", 260);

                slides.Add(new NotebookSlideExportItemDto
                {
                    Order = order,
                    SlideId = GetString(slide, "id") ?? $"slide-{order}",
                    Title = title,
                    Bullets = bullets,
                    HasSpeakerNotes = !string.IsNullOrWhiteSpace(speakerNotes),
                    SpeakerNotes = string.IsNullOrWhiteSpace(speakerNotes) ? null : speakerNotes,
                    SourceLabel = Clip(GetString(slide, "sourceLabel"), 120),
                    VisualSuggestion = Clip(GetString(slide, "visualSuggestion"), 220),
                    CheckpointQuestion = string.IsNullOrWhiteSpace(checkpoint) ? null : checkpoint,
                    MisconceptionWarning = Clip(GetString(slide, "misconceptionWarning"), 260),
                    AccessibilitySummary = accessibility
                });
            }

            return slides;
        }
        catch
        {
            return Array.Empty<NotebookSlideExportItemDto>();
        }
    }

    private static string BuildMarkdown(NotebookSlideExportPreviewDto preview, bool includeNotes)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {preview.DeckTitle}");
        builder.AppendLine();
        builder.AppendLine("Export type: deterministic study deck package");
        builder.AppendLine($"Surface: {preview.Surface}");
        builder.AppendLine($"Context type: {preview.ContextType}");
        builder.AppendLine($"Export scope: {preview.ExportScope}");
        if (preview.Surface == "wiki" && preview.WikiPageId.HasValue)
            builder.AppendLine($"Wiki page id: {preview.WikiPageId}");
        if (preview.Surface == "orkalm" && preview.SourceId.HasValue)
            builder.AppendLine($"Source id: {preview.SourceId}");
        builder.AppendLine("Cross-surface sync: disabled");
        builder.AppendLine($"Slide count: {preview.SlideCount}");
        builder.AppendLine($"Source basis: {preview.SourceBasis}");
        builder.AppendLine($"Source readiness: {preview.SourceReadiness}");
        builder.AppendLine($"Export readiness: {preview.ExportReadiness}");
        builder.AppendLine($"Accessibility: {preview.AccessibilitySummary}");
        builder.AppendLine();
        foreach (var warning in preview.Warnings.Take(8))
            builder.AppendLine($"> Warning: {warning}");
        if (preview.Warnings.Count > 0) builder.AppendLine();

        foreach (var slide in preview.Slides)
        {
            builder.AppendLine($"## {slide.Order}. {slide.Title}");
            builder.AppendLine();
            foreach (var bullet in slide.Bullets)
                builder.AppendLine($"- {bullet}");
            if (!string.IsNullOrWhiteSpace(slide.SourceLabel))
                builder.AppendLine($"- Source label: {slide.SourceLabel}");
            if (!string.IsNullOrWhiteSpace(slide.VisualSuggestion))
                builder.AppendLine($"- Visual hint: {slide.VisualSuggestion}");
            if (!string.IsNullOrWhiteSpace(slide.CheckpointQuestion))
                builder.AppendLine($"- Checkpoint: {slide.CheckpointQuestion}");
            if (!string.IsNullOrWhiteSpace(slide.MisconceptionWarning))
                builder.AppendLine($"- Misconception note: {slide.MisconceptionWarning}");
            if (includeNotes && !string.IsNullOrWhiteSpace(slide.SpeakerNotes))
                builder.AppendLine($"- Speaker notes: {slide.SpeakerNotes}");
            builder.AppendLine($"- Accessibility: {slide.AccessibilitySummary}");
            builder.AppendLine();
        }

        builder.AppendLine("Note: This export is a study deck package. It does not claim official curriculum coverage or learning outcomes.");
        return builder.ToString();
    }

    private static string BuildHtml(NotebookSlideExportPreviewDto preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"tr\"><head><meta charset=\"utf-8\"><title>");
        builder.Append(WebUtility.HtmlEncode(preview.DeckTitle));
        builder.AppendLine("</title></head><body>");
        builder.Append("<h1>").Append(WebUtility.HtmlEncode(preview.DeckTitle)).AppendLine("</h1>");
        builder.Append("<p><strong>Export type:</strong> deterministic study deck package</p>");
        builder.Append("<p><strong>Surface:</strong> ").Append(WebUtility.HtmlEncode(preview.Surface)).AppendLine("</p>");
        builder.Append("<p><strong>Context type:</strong> ").Append(WebUtility.HtmlEncode(preview.ContextType)).AppendLine("</p>");
        builder.Append("<p><strong>Export scope:</strong> ").Append(WebUtility.HtmlEncode(preview.ExportScope)).AppendLine("</p>");
        if (preview.Surface == "wiki" && preview.WikiPageId.HasValue)
            builder.Append("<p><strong>Wiki page id:</strong> ").Append(WebUtility.HtmlEncode(preview.WikiPageId.Value.ToString())).AppendLine("</p>");
        if (preview.Surface == "orkalm" && preview.SourceId.HasValue)
            builder.Append("<p><strong>Source id:</strong> ").Append(WebUtility.HtmlEncode(preview.SourceId.Value.ToString())).AppendLine("</p>");
        builder.Append("<p><strong>Cross-surface sync:</strong> disabled</p>");
        builder.Append("<p><strong>Slide count:</strong> ").Append(preview.SlideCount).AppendLine("</p>");
        builder.Append("<p><strong>Source basis:</strong> ").Append(WebUtility.HtmlEncode(preview.SourceBasis)).AppendLine("</p>");
        builder.Append("<p><strong>Source readiness:</strong> ").Append(WebUtility.HtmlEncode(preview.SourceReadiness)).AppendLine("</p>");
        builder.Append("<p><strong>Export readiness:</strong> ").Append(WebUtility.HtmlEncode(preview.ExportReadiness)).AppendLine("</p>");
        builder.Append("<p><strong>Accessibility:</strong> ").Append(WebUtility.HtmlEncode(preview.AccessibilitySummary)).AppendLine("</p>");
        foreach (var warning in preview.Warnings.Take(8))
            builder.Append("<p><strong>Warning:</strong> ").Append(WebUtility.HtmlEncode(warning)).AppendLine("</p>");
        foreach (var slide in preview.Slides)
        {
            builder.Append("<section><h2>").Append(slide.Order).Append(". ").Append(WebUtility.HtmlEncode(slide.Title)).AppendLine("</h2><ul>");
            foreach (var bullet in slide.Bullets)
                builder.Append("<li>").Append(WebUtility.HtmlEncode(bullet)).AppendLine("</li>");
            if (!string.IsNullOrWhiteSpace(slide.SourceLabel))
                builder.Append("<li>Source label: ").Append(WebUtility.HtmlEncode(slide.SourceLabel)).AppendLine("</li>");
            if (!string.IsNullOrWhiteSpace(slide.VisualSuggestion))
                builder.Append("<li>Visual hint: ").Append(WebUtility.HtmlEncode(slide.VisualSuggestion)).AppendLine("</li>");
            if (!string.IsNullOrWhiteSpace(slide.CheckpointQuestion))
                builder.Append("<li>Checkpoint: ").Append(WebUtility.HtmlEncode(slide.CheckpointQuestion)).AppendLine("</li>");
            if (!string.IsNullOrWhiteSpace(slide.MisconceptionWarning))
                builder.Append("<li>Misconception note: ").Append(WebUtility.HtmlEncode(slide.MisconceptionWarning)).AppendLine("</li>");
            if (!string.IsNullOrWhiteSpace(slide.SpeakerNotes))
                builder.Append("<li>Speaker notes: ").Append(WebUtility.HtmlEncode(slide.SpeakerNotes)).AppendLine("</li>");
            builder.Append("<li>Accessibility: ").Append(WebUtility.HtmlEncode(slide.AccessibilitySummary)).AppendLine("</li>");
            builder.AppendLine("</ul></section>");
        }
        builder.AppendLine("<p>Note: This is a safe study deck export package, not an official curriculum or outcome claim.</p>");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string BuildManifest(NotebookSlideExportPreviewDto preview, bool hasManifestArtifact)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Slide export manifest");
        builder.AppendLine();
        builder.AppendLine($"Deck: {preview.DeckTitle}");
        builder.AppendLine($"Surface: {preview.Surface}");
        builder.AppendLine($"Context type: {preview.ContextType}");
        builder.AppendLine($"Export scope: {preview.ExportScope}");
        if (preview.Surface == "wiki" && preview.WikiPageId.HasValue)
            builder.AppendLine($"Wiki page id: {preview.WikiPageId}");
        if (preview.Surface == "orkalm" && preview.SourceId.HasValue)
            builder.AppendLine($"Source id: {preview.SourceId}");
        builder.AppendLine("Cross-surface sync: disabled");
        builder.AppendLine($"Slide count: {preview.SlideCount}");
        builder.AppendLine($"Source basis: {preview.SourceBasis}");
        builder.AppendLine($"Source readiness: {preview.SourceReadiness}");
        builder.AppendLine($"Export readiness: {preview.ExportReadiness}");
        builder.AppendLine($"Manifest artifact present: {(hasManifestArtifact ? "yes" : "no")}");
        builder.AppendLine($"PPTX status: pptx_not_enabled");
        builder.AppendLine($"Accessibility: {preview.AccessibilitySummary}");
        if (preview.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            foreach (var warning in preview.Warnings.Take(8))
                builder.AppendLine($"- {warning}");
        }

        if (preview.Slides.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Slides:");
            foreach (var slide in preview.Slides.Take(20))
                builder.AppendLine($"- {slide.Order}. {slide.Title} | checkpoint={(string.IsNullOrWhiteSpace(slide.CheckpointQuestion) ? "missing" : "present")} | notes={(slide.HasSpeakerNotes ? "present" : "missing")}");
        }

        builder.AppendLine();
        builder.AppendLine("PPTX export is not enabled; use slide preview, markdown, or escaped HTML export package.");
        return builder.ToString();
    }

    private static NotebookExportSafetyDto ValidateExportContent(string content)
    {
        var blocked = new[]
        {
            "rawPrompt", "hiddenPrompt", "systemPrompt", "rawProviderPayload", "rawSourceChunk",
            "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret=", "secret:",
            "ownerUserId", "answerKey", "correctAnswer", "stackTrace", "C:\\", "\\Users\\",
            "/home/", "<script", "<iframe", "<object", "<embed", "javascript:", "onerror=",
            "onload=", "onclick="
        }.Where(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new NotebookExportSafetyDto
        {
            Status = blocked.Length == 0 ? "safe" : "blocked",
            BlockingIssues = blocked.Select(term => $"blocked_term:{term}").ToArray(),
            Warnings = content.Length > 20000 ? ["export_content_is_large"] : Array.Empty<string>()
        };
    }

    private static NotebookExportAccessibilityDto BuildAccessibility(NotebookSlideExportPreviewDto preview)
    {
        var issues = new List<string>();
        if (preview.Slides.Count == 0) issues.Add("missing_slide_level_accessibility");
        if (preview.Slides.Any(s => string.IsNullOrWhiteSpace(s.AccessibilitySummary))) issues.Add("slide_missing_accessibility_summary");
        return new NotebookExportAccessibilityDto
        {
            Status = issues.Count == 0 ? "usable" : "needs_review",
            Summary = preview.AccessibilitySummary,
            HasSpeakerNotes = preview.Slides.Any(s => s.HasSpeakerNotes),
            HasCheckpointQuestions = preview.Slides.Any(s => !string.IsNullOrWhiteSpace(s.CheckpointQuestion)),
            HasTextFallback = true,
            Issues = issues
        };
    }

    private async Task RecordTelemetryAsync(Guid userId, LearningNotebookPack pack, string operation, string status, int warningCount, CancellationToken ct)
    {
        try
        {
            await _telemetry.RecordEventAsync(userId, new LearningRuntimeEventRequestDto
            {
                TopicId = pack.TopicId,
                SessionId = pack.SessionId,
                CorrelationId = $"notebook-export:{pack.Id:N}",
                Category = "notebook_export",
                Operation = operation,
                Status = status == "failed" ? "failed" : status == "unsupported" ? "skipped" : "succeeded",
                Severity = status is "failed" or "unsupported" ? "warning" : "info",
                SafeMessage = "Notebook export operation was recorded safely.",
                SafeMetadata = new Dictionary<string, string>
                {
                    ["packId"] = pack.Id.ToString(),
                    ["packType"] = pack.PackType,
                    ["evidenceStatus"] = pack.EvidenceStatus,
                    ["warningCount"] = warningCount.ToString()
                }
            }, ct);
        }
        catch
        {
            // Export must not fail because telemetry could not be recorded.
        }
    }

    private static string NormalizeFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "markdown";
        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string SourceBasisFor(string evidenceStatus) =>
        evidenceStatus switch
        {
            "source_grounded" or "mixed" => "source_grounded",
            "wiki_backed" => "wiki_backed",
            _ => "evidence_insufficient"
        };

    private static ExportSurfaceContext ResolveSurfaceContext(LearningNotebookPack pack)
    {
        Guid? sourceId = null;
        string? sourceSurface = null;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(pack.SafeMetadataJson) ? "{}" : pack.SafeMetadataJson);
            var root = document.RootElement;
            if (root.TryGetProperty("sourceId", out var sourceElement) &&
                sourceElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(sourceElement.GetString(), out var parsedSourceId))
            {
                sourceId = parsedSourceId;
            }
            if (root.TryGetProperty("sourceSurface", out var surfaceElement) &&
                surfaceElement.ValueKind == JsonValueKind.String)
            {
                sourceSurface = surfaceElement.GetString();
            }
        }
        catch
        {
            sourceId = null;
            sourceSurface = null;
        }

        var surface = IsSourceSurface(sourceSurface) || pack.PackType is "source_digest" or "source_notebook" or "source_review"
            ? "orkalm"
            : "wiki";
        return surface == "orkalm"
            ? new ExportSurfaceContext("orkalm", "source_notebook", null, sourceId, "orkalm_source_export_scope")
            : new ExportSurfaceContext("wiki", "wiki_page", pack.WikiPageId, null, "wiki_lesson_export_scope");
    }

    private static bool IsSourceSurface(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        return key is "source" or "source_notebook" or "source_collection" or "orkalm" or "orkalm_source";
    }

    private static IReadOnlyList<string> TemplateKeysFor(string surface) =>
        surface == "orkalm"
            ? ["source_briefing", "source_study_guide", "source_glossary", "source_timeline", "source_quiz", "source_flashcards", "source_slides", "source_diagram", "source_export"]
            : ["wiki_briefing", "wiki_study_guide", "wiki_glossary", "wiki_timeline", "wiki_quiz", "wiki_flashcards", "wiki_slides", "wiki_diagram", "wiki_export"];

    private static IReadOnlyList<string> SearchFilterKeysFor(string surface) =>
        surface == "orkalm"
            ? ["surface:orkalm", "context_type:source_notebook", "source_id", "citation_status", "source_tag", "cross_surface_sync:false"]
            : ["surface:wiki", "context_type:wiki_page", "wiki_page_id", "concept_key", "wiki_tag", "cross_surface_sync:false"];

    private static IReadOnlyList<string> InternalConnectionKeysFor(string surface) =>
        surface == "orkalm"
            ? ["source_notebook", "citation", "source_qa", "source_practice", "cross_surface_sync:disabled"]
            : ["wiki_page", "plan_step", "tutor_trace", "question_bank_trace", "wiki_learning_trace", "cross_surface_sync:disabled"];

    private static int GetInt(JsonElement element, string property, int fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(NotBlank)
            .Select(item => item!)
            .ToArray();
    }

    private static IReadOnlyList<Guid> ParseGuids(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Guid>();
        try { return JsonSerializer.Deserialize<Guid[]>(json, JsonOptions) ?? Array.Empty<Guid>(); }
        catch { return Array.Empty<Guid>(); }
    }

    private static IReadOnlyList<string> ParseStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string Slug(string value)
    {
        var cleaned = new string(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        while (cleaned.Contains("--", StringComparison.Ordinal)) cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        cleaned = cleaned.Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "orka-notebook-export" : Clip(cleaned, 80);
    }

    private static string Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private readonly record struct ExportContext(
        LearningNotebookPack Pack,
        LearningArtifact? SlideArtifact,
        LearningArtifact? ManifestArtifact);

    private readonly record struct ExportSurfaceContext(
        string Surface,
        string ContextType,
        Guid? WikiPageId,
        Guid? SourceId,
        string ExportScope);
}
