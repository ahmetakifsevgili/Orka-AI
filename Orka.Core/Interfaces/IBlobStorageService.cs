using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

/// <summary>
/// Görsel dosya yükleme servisi arayüzü.
///
/// FAZ 3: Çok Modlu (Multimodal) Mimari
/// Tasarım Kararı: Base64 JSON gömme YOK → Large Object Heap (LOH) şişmesi önlendi.
/// Dosyalar Stream ile doğrudan buluta aktarılır, LLM'e sadece URL verilir.
///
/// Geliştirme: LocalBlobStorageService (wwwroot/uploads/)
/// Production: AzureBlobStorageService (Azure Blob Storage + SAS Token)
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Dosyayı stream ile buluta yükler. Belleğe almaz → LOH güvenli.
    /// </summary>
    /// <returns>LLM'in erişebileceği public veya SAS token URL'i</returns>
    Task<string> UploadImageAsync(
        Stream fileStream,
        string fileName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Blob'u siler. Multimodal geçmişten resim kaldırıldığında çağrılır.
    /// </summary>
    Task DeleteBlobAsync(string blobUrl, CancellationToken cancellationToken = default);
}
