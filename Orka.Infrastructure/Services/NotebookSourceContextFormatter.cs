using System.Text;
using Orka.Core.Entities;

namespace Orka.Infrastructure.Services;

public static class NotebookSourceContextFormatter
{
    public const int DefaultMaxContextChars = 7000;

    public static string BuildSourceContext(IEnumerable<SourceChunk> chunks, int maxContextChars = DefaultMaxContextChars)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            var block = $"""
                [doc:{chunk.LearningSourceId}:p{chunk.PageNumber}] chunk:{chunk.ChunkIndex}
                {chunk.Text}

                """;

            if (sb.Length > 0 && sb.Length + block.Length > maxContextChars)
                break;

            sb.Append(block);
        }

        return sb.ToString();
    }

    public static float ScoreLexical(SourceChunk chunk, string question)
    {
        var words = question
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2)
            .Select(NormalizeToken)
            .Where(w => w.Length > 0)
            .Distinct()
            .ToList();
        if (words.Count == 0) return 0;

        var text = NormalizeToken(chunk.Text);
        return words.Count(w => text.Contains(w, StringComparison.Ordinal)) / (float)words.Count;
    }

    public static string SourceMissingAnswer() =>
        "Bu bilgi yuklenen belgede yer almiyor veya kaynak parcasi henuz okunabilir durumda degil.";

    private static string NormalizeToken(string value) =>
        new(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray());
}
