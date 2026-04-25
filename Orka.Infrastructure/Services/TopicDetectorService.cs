using System;
using System.Linq;
using System.Threading.Tasks;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class TopicDetectorService : ITopicDetectorService
{

    private static readonly string[] NewTopicPatterns =
    [
        "öğrenmek istiyorum",
        "öğrenmek istiyorum",
        "öğretir misin",
        "öğret bana",
        "hakkında bilgi ver",
        "hakkında bilgi verir misin",
        "nasıl öğrenirim",
        "çalışmak istiyorum",
        "anlamak istiyorum",
        "konusunu çalış",
        "konusu nedir",
        "nedir anlat",
        "nedir açıkla",
        "i want to learn",
        "teach me about",
        "tell me about",
        "explain what is",
        "how do i learn"
    ];

    private readonly IGroqService _groqService;

    public TopicDetectorService(IGroqService groqService)
    {
        _groqService = groqService;
    }

    public bool IsNewTopic(string message)
    {
        var lower = message.ToLowerInvariant();
        return NewTopicPatterns.Any(p => lower.Contains(p));
    }

    public async Task<(string Topic, string Category)> ExtractTopicNameAsync(string message)
    {
        // Önce basit keyword extraction dene
        var simpleExtract = TrySimpleExtract(message);
        if (!string.IsNullOrEmpty(simpleExtract))
            return (simpleExtract, "Genel");

        // Belirsizse Groq'a sor (SemanticRoute üzerinden)
        var route = await _groqService.SemanticRouteAsync(message);
        return (route.ExtractedTopic ?? "Bilinmeyen Konu", route.Category ?? "Genel");
    }

    private static string? TrySimpleExtract(string message)
    {
        var lower = message.ToLowerInvariant();

        var patterns = new[]
        {
            ("öğrenmek istiyorum", ""),
            ("hakkında bilgi ver", ""),
            ("öğret bana", ""),
            ("nasıl öğrenirim", ""),
            ("çalışmak istiyorum", ""),
            ("anlamak istiyorum", "")
        };

        foreach (var (pattern, _) in patterns)
        {
            var idx = lower.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                var candidate = message[..idx].Trim().TrimEnd([',', '.', '!', '?', '\'', '"']);
                if (candidate.Length > 2 && candidate.Length < 100)
                    return candidate;
            }
        }

        return null;
    }
}
