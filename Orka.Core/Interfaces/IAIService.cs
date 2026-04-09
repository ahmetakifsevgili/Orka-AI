namespace Orka.Core.Interfaces;

/// <summary>
/// Tüm AI sağlayıcıları için ortak polimorfik arayüz.
/// </summary>
public interface IAIService
{
    Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}

/// <summary>
/// Google Gemini — Primary Smart Router.
/// Görev tipine göre farklı model konfigürasyonu uygular.
/// </summary>
public interface IGeminiService
{
    /// <summary>Görev tipini otomatik tespit ederek uygun Gemini modelini seçer.</summary>
    Task<string> GenerateSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}

/// <summary>Cohere — kurumsal düzeyde içerik üretimi ve özetleme.</summary>
public interface ICohereService : IAIService { }

/// <summary>HuggingFace Router — dinamik yük dengeleme ile Llama 3.1-8B.</summary>
public interface IHuggingFaceService : IAIService { }
