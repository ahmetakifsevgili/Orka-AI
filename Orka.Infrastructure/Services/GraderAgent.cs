using Microsoft.Extensions.Logging;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Değerlendirici Ajan (Grader & Evaluator)
/// LLM-as-a-judge mimarisiyle çalışır. Retriever'dan (RAG) gelen verinin
/// veya Teacher ajanının cevabının kalite kontrolünü ve halüsinasyon testini yapar.
/// LangGraph mimarisindeki "Grader Node" rolünü üstlenir.
/// </summary>
public interface IGraderAgent
{
    /// <summary>
    /// Sağlanan bilginin, hedeflenen bağlam (context) ve konuya ne kadar uygun olduğunu puanlar.
    /// 0 ile 1.0 arası bir "Relevancy Score" (Alaka Durumu) veya geçiş izni verir.
    /// </summary>
    Task<bool> IsContextRelevantAsync(string topic, string retrievedContext, string? goalContext = null, CancellationToken ct = default);
}

public class GraderAgent : IGraderAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<GraderAgent> _logger;

    public GraderAgent(IAIAgentFactory factory, ILogger<GraderAgent> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<bool> IsContextRelevantAsync(string topic, string retrievedContext, string? goalContext = null, CancellationToken ct = default)
    {
        var prompt = $$"""
            Sen katı bir akademik gözlemcisin (Grader). 
            Sana verilen 'Gelen İçerik' metninin, belirtilen 'Konu: {{topic}}' ile ne ölçüde örtüştüğünü ve doğruluğunu kontrol edeceksin.
            Öğrencinin Hedefi: {{(string.IsNullOrWhiteSpace(goalContext) ? "Genel" : goalContext)}}
            Lütfen içeriğin öğrencinin hedefine (varsa) ve konuya uygunluğunu denetle.
            Eğer içerik alakasızsa, yanıltıcıysa veya hedef kitle için çok zayıfsa 'REJECT' yaz.
            Eğer içerik faydalıysa, bağlamsal olarak yüksek bir örtüşme (Hit Rate) taşıyorsa 'APPROVE' yaz.
            
            SADECE REJECT VEYA APPROVE YAZ, AÇIKLAMA YAPMA.
            """;

        _logger.LogInformation("[GraderAgent] Bilgi doğruluğu denetleniyor. Konu: {Topic}", topic);
        
        try
        {
            var result = await _factory.CompleteChatAsync(AgentRole.Grader, prompt, $"[Gelen İçerik]\n{retrievedContext}", ct);
            
            if (result.Trim().ToUpperInvariant().Contains("APPROVE"))
            {
                _logger.LogInformation("[GraderAgent] Analiz başarılı, onay verildi.");
                return true;
            }
            
            _logger.LogWarning("[GraderAgent] Analiz başarısız (REJECT). İçerik konuyla örtüşmüyor.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GraderAgent] Kontrol esnasında hata oluştu. Tedbir amaçlı FALSE dönülüyor.");
            return false;
        }
    }
}
