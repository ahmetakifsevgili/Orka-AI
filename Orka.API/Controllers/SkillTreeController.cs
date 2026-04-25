using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

/// <summary>
/// FAZ 4: Dinamik Yetenek Ağacı API Kontrolörü
/// xyflow (React Flow) + dagre.js ile animasyonlu DAG haritası için endpoint'ler.
///
/// Akış:
/// EvaluatorAgent → ProposeRemedialNodeAsync → SkillTreeService.AddNodeAsync (DFS kontrol)
/// → NODE_ADDED SSE event → React → dagre.js koordinatlar → xyflow animasyon
/// </summary>
[Authorize]
[ApiController]
[Route("api/skilltree")]
public class SkillTreeController : ControllerBase
{
    private readonly ISkillTreeService _skillTreeService;
    private readonly ILogger<SkillTreeController> _logger;

    public SkillTreeController(
        ISkillTreeService skillTreeService,
        ILogger<SkillTreeController> logger)
    {
        _skillTreeService = skillTreeService;
        _logger = logger;
    }

    /// <summary>
    /// Kullanıcının tüm Skill Tree'sini döndürür.
    /// Response: { nodes: SkillNode[], edges: SkillEdge[] }
    /// xyflow bunu Node[] ve Edge[] olarak tüketir.
    /// dagre.js x/y koordinatlarını otomatik hesaplar.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSkillTree()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            var nodes = await _skillTreeService.GetAllNodesAsync(userId);
            var edges = await _skillTreeService.GetAllEdgesAsync(userId);

            return Ok(new { nodes, edges });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SkillTreeController] GetSkillTree hatası. UserId={UserId}", userId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Düğümü kilit aç (öğrenci tamamladı).
    /// UI'da düğüm parlıyor/renk değiştiriyor animasyonu tetiklenir.
    /// </summary>
    [HttpPost("unlock/{nodeId:guid}")]
    public async Task<IActionResult> UnlockNode([FromRoute] Guid nodeId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            var unlocked = await _skillTreeService.UnlockNodeAsync(nodeId, userId);
            return unlocked
                ? Ok(new { message = "Düğüm açıldı.", nodeId })
                : NotFound(new { error = "Düğüm bulunamadı." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Bir düğümün tüm torunlarını döndürür (Closure Table O(1) sorgusu).
    /// xyflow'da alt ağaç vurgulama için kullanılabilir.
    /// </summary>
    [HttpGet("{nodeId:guid}/descendants")]
    public async Task<IActionResult> GetDescendants([FromRoute] Guid nodeId)
    {
        try
        {
            var descendants = await _skillTreeService.GetDescendantsAsync(nodeId);
            return Ok(descendants);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
