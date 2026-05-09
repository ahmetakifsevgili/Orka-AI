using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/standards")]
public sealed class StandardsController : ControllerBase
{
    private readonly IStandardsAlignmentService _alignment;
    private readonly IStandardsValidationService _validation;
    private readonly IStandardsExportService _export;

    public StandardsController(
        IStandardsAlignmentService alignment,
        IStandardsValidationService validation,
        IStandardsExportService export)
    {
        _alignment = alignment;
        _validation = validation;
        _export = export;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("topic/{topicId:guid}/summary")]
    public async Task<ActionResult<StandardsSummaryDto>> GetSummary(Guid topicId, CancellationToken ct)
    {
        return Ok(await _alignment.GetSummaryAsync(GetUserId(), topicId, ct));
    }

    [HttpPost("topic/{topicId:guid}/validate")]
    public async Task<ActionResult<StandardsValidationRunDto>> Validate(Guid topicId, CancellationToken ct)
    {
        return Ok(await _validation.ValidateAsync(GetUserId(), topicId, ct));
    }

    [HttpPost("topic/{topicId:guid}/export")]
    public async Task<ActionResult<StandardsExportRunDto>> Export(Guid topicId, [FromQuery] string exportType = "combined", CancellationToken ct = default)
    {
        return Ok(await _export.ExportAsync(GetUserId(), topicId, exportType, ct));
    }
}
