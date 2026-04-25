using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

/// <summary>
/// FAZ 3: Çok Modlu (Multimodal) Görsel Yükleme Kontrolörü
///
/// LOH Güvenli Akış:
///   Frontend → multipart/form-data → IFormFile → Stream → BlobStorage → URL
///   Base64 JSON kullanılmaz → Large Object Heap (LOH) şişmesi önlendi.
///   GC Gen2 duraklamaları (Stop-the-World pauses) ortadan kalktı.
///
/// Kullanım:
///   1. Bu endpoint'e resim yükle → URL al
///   2. URL'i /api/chat/multimodal endpoint'ine ContentItemDto içinde gönder
/// </summary>
[Authorize]
[ApiController]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<UploadController> _logger;

    // Maksimum dosya boyutu: 10MB
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public UploadController(
        IBlobStorageService blobStorage,
        ILogger<UploadController> logger)
    {
        _blobStorage = blobStorage;
        _logger = logger;
    }

    /// <summary>
    /// Görsel dosya yükler ve erişilebilir URL döndürür.
    ///
    /// Güvenlik:
    /// - Sadece resim MIME türleri kabul edilir
    /// - 10MB dosya boyutu sınırı
    /// - UserId bazlı klasör izolasyonu
    ///
    /// Optimistic UI desteği:
    /// React, bu endpoint'e yükleme yaparken kullanıcıya thumbnail gösterir.
    /// Yükleme bitmeden kullanıcı mesaj yazabilir, yükleme bitince mesaj gönderilir.
    /// </summary>
    [HttpPost("image")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Dosya boş veya yüklenmedi." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = $"Dosya boyutu {MaxFileSizeBytes / 1024 / 1024}MB sınırını aşıyor." });

        // MIME türü kontrolü
        var allowedMimeTypes = new[]
        {
            "image/jpeg", "image/jpg", "image/png",
            "image/gif", "image/webp", "image/bmp"
        };

        if (!Array.Exists(allowedMimeTypes, m => m.Equals(file.ContentType, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { error = $"Desteklenmeyen dosya türü: {file.ContentType}. Sadece resim dosyaları kabul edilir." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? throw new UnauthorizedAccessException("Kullanıcı kimliği bulunamadı.");

        try
        {
            // Stream ile yükle — belleğe alma
            await using var stream = file.OpenReadStream();
            var imageUrl = await _blobStorage.UploadImageAsync(
                stream,
                file.FileName,
                userId,
                HttpContext.RequestAborted);

            _logger.LogInformation(
                "[UploadController] Resim yüklendi. UserId={UserId} FileName={FileName} Size={Size}KB",
                userId, file.FileName, file.Length / 1024);

            return Ok(new
            {
                imageUrl,
                fileName = file.FileName,
                sizeKb = file.Length / 1024
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UploadController] Resim yükleme başarısız. UserId={UserId}", userId);
            return StatusCode(500, new { error = "Resim yüklenirken bir hata oluştu." });
        }
    }
}
