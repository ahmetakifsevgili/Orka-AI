using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/notebook-studio")]
public sealed class NotebookStudioController : ControllerBase
{
    private readonly ILearningNotebookStudioService _studio;
    private readonly INotebookExportService _exports;

    public NotebookStudioController(ILearningNotebookStudioService studio, INotebookExportService exports)
    {
        _studio = studio;
        _exports = exports;
    }

    [HttpGet("topic/{topicId:guid}/packs")]
    public async Task<ActionResult<LearningNotebookPackListDto>> ListTopicPacks(
        Guid topicId,
        [FromQuery] Guid? sessionId,
        [FromQuery] Guid? wikiPageId,
        [FromQuery] string? surface,
        [FromQuery] Guid? sourceId,
        CancellationToken ct)
    {
        return Ok(await _studio.ListPacksAsync(CurrentUserId(), topicId, sessionId, wikiPageId, surface, sourceId, ct));
    }

    [HttpGet("packs/{packId:guid}")]
    public async Task<ActionResult<LearningNotebookPackDto>> GetPack(Guid packId, CancellationToken ct)
    {
        var pack = await _studio.GetPackAsync(CurrentUserId(), packId, ct);
        return pack == null ? NotFound() : Ok(pack);
    }

    [HttpPost("topic/{topicId:guid}/milestone-pack")]
    public async Task<ActionResult<LearningNotebookPackDto>> BuildMilestonePack(
        Guid topicId,
        [FromBody] LearningNotebookPackRequestDto? request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _studio.BuildMilestonePackAsync(CurrentUserId(), topicId, request ?? new(), ct));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("wiki-page/{pageId:guid}/pack")]
    public async Task<ActionResult<LearningNotebookPackDto>> BuildWikiPagePack(
        Guid pageId,
        [FromBody] LearningNotebookPackRequestDto? request,
        CancellationToken ct)
    {
        var pack = await _studio.BuildWikiPagePackAsync(CurrentUserId(), pageId, request ?? new(), ct);
        return pack == null ? NotFound() : Ok(pack);
    }

    [HttpPost("sources/{sourceId:guid}/pack")]
    public async Task<ActionResult<LearningNotebookPackDto>> BuildSourcePack(
        Guid sourceId,
        [FromBody] LearningNotebookPackRequestDto? request,
        CancellationToken ct)
    {
        var pack = await _studio.BuildSourcePackAsync(CurrentUserId(), sourceId, request ?? new(), ct);
        return pack == null ? NotFound() : Ok(pack);
    }

    [HttpPost("topic/{topicId:guid}/source-pack")]
    public async Task<ActionResult<LearningNotebookPackDto>> BuildTopicSourcePack(
        Guid topicId,
        [FromBody] LearningNotebookPackRequestDto? request,
        CancellationToken ct)
    {
        var pack = await _studio.BuildTopicSourcePackAsync(CurrentUserId(), topicId, request ?? new(), ct);
        return pack == null ? NotFound() : Ok(pack);
    }

    [HttpPost("packs/{packId:guid}/refresh")]
    public async Task<ActionResult<LearningNotebookPackDto>> RefreshPack(Guid packId, CancellationToken ct)
    {
        var pack = await _studio.RefreshPackAsync(CurrentUserId(), packId, ct);
        return pack == null ? NotFound() : Ok(pack);
    }

    [HttpPost("packs/{packId:guid}/artifact")]
    public async Task<ActionResult<LearningArtifactDto>> BuildArtifact(
        Guid packId,
        [FromBody] LearningNotebookArtifactRequestDto? request,
        CancellationToken ct)
    {
        var artifact = await _studio.BuildArtifactAsync(CurrentUserId(), packId, request ?? new(), ct);
        return artifact == null ? NotFound() : Ok(artifact);
    }

    [HttpGet("packs/{packId:guid}/export/preview")]
    public async Task<ActionResult<NotebookSlideExportPreviewDto>> GetExportPreview(Guid packId, CancellationToken ct)
    {
        var preview = await _exports.BuildSlidePreviewAsync(CurrentUserId(), packId, ct);
        return preview == null ? NotFound() : Ok(preview);
    }

    [HttpPost("packs/{packId:guid}/export")]
    public async Task<ActionResult<NotebookExportResultDto>> ExportPack(
        Guid packId,
        [FromBody] NotebookExportRequestDto? request,
        CancellationToken ct)
    {
        var result = await _exports.ExportAsync(CurrentUserId(), packId, request ?? new(), ct);
        return result == null ? NotFound() : Ok(result);
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
