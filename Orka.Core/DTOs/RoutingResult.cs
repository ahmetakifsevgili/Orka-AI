namespace Orka.Core.DTOs;

/// <summary>
/// Semantic Router'ın tek bir LLM çağrısından döndüğü sonuç.
/// Intent + Konu adı + Plan gereksinimi + Kullanıcı anlama durumu.
/// </summary>
public class RoutingResult
{
    /// <summary>
    /// Tespit edilen intent: greeting, explain, research, quiz, interview, summary, plan, new_topic, general
    /// </summary>
    public string Intent { get; set; } = "general";

    /// <summary>
    /// Mesajdan çıkarılan konu adı (örn: "C# Mülakat Hazırlığı", "Python").
    /// Eğer konu yoksa null.
    /// </summary>
    public string? ExtractedTopic { get; set; }

    /// <summary>
    /// Kullanıcı yeni bir öğrenme planı mı istiyor?
    /// "öğrenmek istiyorum", "mülakatım var", "çalışalım" → true
    /// </summary>
    public bool RequiresNewPlan { get; set; }

    /// <summary>
    /// Kullanıcı "anladım", "tamam", "geçelim" gibi anlama belirten kelime mi kullandı?
    /// true ise anında WikiBlock yaratılır (5 mesaj beklenmez).
    /// </summary>
    public bool UnderstoodConcept { get; set; }

    /// <summary>
    /// AI'nın tavsiye ettiği yeni faz (örn: Assessment, Planning).
    /// </summary>
    public string? SuggestedPhase { get; set; }

    /// <summary>
    /// AI'nın hafızaya (Metadata) eklenmesini istediği yeni JSON verileri.
    /// </summary>
    public string? MetadataDelta { get; set; }

    /// <summary>
    /// Routing yöntemi: "slash" (slash command), "semantic" (LLM), "fallback"
    /// </summary>
    public string Method { get; set; } = "semantic";

    public string Category { get; set; } = "Chat"; // Plan veya Chat
}
