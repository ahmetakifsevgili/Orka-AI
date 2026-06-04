namespace Orka.Core.Interfaces;

/// <summary>
/// Tüm AI sağlayıcıları için ortak polimorfik arayüz.
/// </summary>
public interface IAIService
{
    Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null);
    IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null);
}

/// <summary>
/// Google Gemini — Primary Smart Router.
/// Görev tipine göre farklı model konfigürasyonu uygular.
/// </summary>
public interface IGeminiService
{
    /// <summary>Görev tipini otomatik tespit ederek uygun Gemini modelini seçer.</summary>
    Task<string> GenerateSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
    
    /// <summary>Spesifik olarak belirli bir modeli (örn. gemma-4-26b-a4b-it) kullanarak içerik üretir.</summary>
    Task<string> GenerateWithModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null);

    /// <summary>Gemini üzerinden streaming (akış) desteği sunar.</summary>
    IAsyncEnumerable<string> StreamSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    /// <summary>Spesifik olarak belirli bir modeli kullanarak streaming (akış) desteği sunar.</summary>
    IAsyncEnumerable<string> StreamWithModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null);
}
