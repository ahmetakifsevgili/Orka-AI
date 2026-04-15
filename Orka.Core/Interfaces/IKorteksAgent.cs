namespace Orka.Core.Interfaces;

/// <summary>
/// Korteks — Orka AI'nın otonom araştırma beyni.
///
/// Microsoft Semantic Kernel üzerinden çalışır.
/// Web'de arama yapar, konuları analiz eder ve Wiki'ye yazar.
/// Tüm bu işlemleri kendi başına "Function Calling" ile yürütür.
///
/// Dış dünyaya açılan tek otonom ajan budur.
/// </summary>
public interface IKorteksAgent
{
    /// <summary>
    /// Verilen konu hakkında otonom araştırma başlatır.
    /// SK Plugin'leri (TavilySearch, Wiki, Topic) otomatik tetiklenir.
    /// Her adımın log çıktısını stream olarak döndürür (Frontend'te adım adım göstermek için).
    /// </summary>
    IAsyncEnumerable<string> RunResearchAsync(
        string topic,
        Guid userId,
        Guid? topicId = null,
        string? fileContext = null,
        CancellationToken ct = default);
}
