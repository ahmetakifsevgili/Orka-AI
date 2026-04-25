using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Geliştirme ortamı için yerel dosya sistemi blob hizmeti.
/// 
/// FAZ 3: LOH Güvenli Görsel Yükleme
/// Resimler {UploadRoot}/{userId}/ klasörüne stream ile kaydedilir.
/// Belleğe alınmaz → GC Gen2 duraklaması riski yok.
///
/// Production geçişi: Bu servisi AzureBlobStorageService ile değiştir.
/// Azure SDK: Azure.Storage.Blobs NuGet paketi gerekir.
/// SAS Token mantığı burada hazır comment olarak bırakıldı.
///
/// NOT: Infrastructure katmanı ASP.NET Core'a bağımlı olamaz.
/// Bu nedenle IWebHostEnvironment yerine IConfiguration üzerinden path alınır.
/// appsettings.json: "Storage:UploadPath" (ör: "wwwroot/uploads")
/// </summary>
public class LocalBlobStorageService : IBlobStorageService
{
    private readonly IConfiguration _config;
    private readonly ILogger<LocalBlobStorageService> _logger;

    private string GetUploadRoot()
    {
        // appsettings.json'dan al, yoksa varsayılan
        return _config["Storage:UploadPath"] ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads");
    }

    public LocalBlobStorageService(
        IConfiguration config,
        ILogger<LocalBlobStorageService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> UploadImageAsync(
        Stream fileStream,
        string fileName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Güvenli dosya adı: userId klasörü + GUID + uzantı
        var safeUserId = SanitizePathSegment(userId);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        // Sadece izin verilen uzantılar
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        if (!Array.Exists(allowedExtensions, e => e == ext))
            throw new InvalidOperationException($"İzin verilmeyen dosya uzantısı: {ext}");

        var uniqueFileName = $"{Guid.NewGuid()}{ext}";
        var uploadRoot = GetUploadRoot();
        var userFolder = Path.Combine(uploadRoot, safeUserId);

        // Klasör yoksa oluştur
        if (!Directory.Exists(userFolder))
            Directory.CreateDirectory(userFolder);

        var filePath = Path.Combine(userFolder, uniqueFileName);

        // STREAM ile yaz — Base64'e çevirme, belleğe alma (LOH güvenli)
        await using var fileWrite = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920, // 80KB buffer — LOH eşiği olan 85KB'ın altında
            useAsync: true);

        await fileStream.CopyToAsync(fileWrite, 81920, cancellationToken);

        _logger.LogInformation(
            "[LocalBlobStorage] Resim yüklendi. UserId={UserId} File={File} Size={Size}KB",
            safeUserId, uniqueFileName,
            fileWrite.Length / 1024);

        // Local URL döndür (Production'da SAS Token URL olur)
        return $"/uploads/{safeUserId}/{uniqueFileName}";

        /* Production Azure implementasyonu için taslak:
        var blobServiceClient = new BlobServiceClient(_connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient("chat-images");
        var blobName = $"{userId}/{Guid.NewGuid()}{ext}";
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken);
        return GenerateSasTokenUrl(blobClient, TimeSpan.FromHours(1));
        */
    }

    public Task DeleteBlobAsync(string blobUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            // Local path'i URL'den çözümle
            var uploadRoot = GetUploadRoot();
            var relativePath = blobUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(uploadRoot, "..", relativePath);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("[LocalBlobStorage] Blob silindi: {Path}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LocalBlobStorage] Blob silinemedi: {Url}", blobUrl);
        }

        return Task.CompletedTask;
    }

    private static string SanitizePathSegment(string input) =>
        string.Concat(input.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
}
