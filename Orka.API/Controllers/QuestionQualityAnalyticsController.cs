using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/question-quality")]
public sealed class QuestionQualityAnalyticsController : ControllerBase
{
    private readonly IQuestionQualityAnalyticsService _analytics;

    public QuestionQualityAnalyticsController(IQuestionQualityAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("questions/{questionId:guid}")]
    public async Task<ActionResult<QuestionItemAnalyticsDto>> GetQuestionAnalytics(Guid questionId, CancellationToken ct)
    {
        var result = await _analytics.GetQuestionAnalyticsAsync(GetUserId(), questionId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("questions/{questionId:guid}/recalculate")]
    public async Task<ActionResult<RecalculateQuestionAnalyticsResultDto>> RecalculateQuestionAnalytics(Guid questionId, CancellationToken ct)
    {
        var result = await _analytics.RecalculateQuestionAnalyticsAsync(GetUserId(), questionId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("questions/{questionId:guid}/signals")]
    public async Task<ActionResult<IReadOnlyList<QuestionQualityReviewSignalDto>>> GetQuestionSignals(Guid questionId, CancellationToken ct)
    {
        if (await _analytics.GetQuestionAnalyticsAsync(GetUserId(), questionId, ct) is null
            && (await _analytics.GetQuestionQualitySignalsAsync(GetUserId(), questionId, ct)).Count == 0)
        {
            return NotFound();
        }

        return Ok(await _analytics.GetQuestionQualitySignalsAsync(GetUserId(), questionId, ct));
    }

    [HttpGet("central-exams/{examCode}")]
    public async Task<ActionResult<CentralExamQualityOverviewDto>> GetCentralExamOverview(string examCode, [FromQuery] string? variantCode, CancellationToken ct)
    {
        var result = await _analytics.GetCentralExamQualityOverviewAsync(GetUserId(), examCode, variantCode, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("central-exams/{examCode}/recalculate")]
    public async Task<ActionResult<RecalculateExamAnalyticsResultDto>> RecalculateCentralExam(string examCode, [FromQuery] string? variantCode, CancellationToken ct)
    {
        return Ok(await _analytics.RecalculateCentralExamAnalyticsAsync(GetUserId(), examCode, variantCode, ct));
    }

    [HttpGet("central-exams/{examCode}/coverage")]
    public async Task<ActionResult<CentralExamBlueprintCoverageDto>> GetCentralExamCoverage(string examCode, [FromQuery] string? variantCode, CancellationToken ct)
    {
        var result = await _analytics.GetBlueprintCoverageAsync(GetUserId(), examCode, variantCode, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
