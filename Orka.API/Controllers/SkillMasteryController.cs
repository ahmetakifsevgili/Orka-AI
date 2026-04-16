using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[ApiController]
[Route("api/skills")]
[Authorize]
public class SkillMasteryController : ControllerBase
{
    private readonly ISkillMasteryService _skillMastery;

    public SkillMasteryController(ISkillMasteryService skillMastery)
    {
        _skillMastery = skillMastery;
    }

    /// <summary>Kullanıcının tüm mastery kayıtlarını döner.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var masteries = await _skillMastery.GetAllMasteriesAsync(userId);

        var result = masteries.Select(m => new
        {
            m.Id,
            m.TopicId,
            m.SubTopicTitle,
            m.MasteredAt,
            m.QuizScore
        });

        return Ok(result);
    }

    /// <summary>Belirli bir konu altındaki mastery kayıtlarını döner.</summary>
    [HttpGet("{topicId:guid}")]
    public async Task<IActionResult> GetByTopic(Guid topicId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var masteries = await _skillMastery.GetMasteriesByTopicAsync(userId, topicId);

        var result = masteries.Select(m => new
        {
            m.Id,
            m.TopicId,
            m.SubTopicTitle,
            m.MasteredAt,
            m.QuizScore
        });

        return Ok(result);
    }
}
