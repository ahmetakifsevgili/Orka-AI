using System.Text.RegularExpressions;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Quiz cevap karşılaştırması için LaTeX/Unicode/Markdown notasyonunu sade
/// ASCII'ye indirger. Hedef: "$\frac{1}{2}$", "1/2" ve "½" aynı kabul edilebilsin.
/// LLM-judge'a hem ham hem normalize formu birlikte verildiği için bu çıktı
/// "kanonik form" değil, "eşdeğerlik ipucu" olarak düşünülür.
/// </summary>
public static class LatexAnswerNormalizer
{
    private static readonly Regex MathDelimiterRegex = new(
        @"\$\$|\$|\\\(|\\\)|\\\[|\\\]", RegexOptions.Compiled);

    private static readonly Regex FracRegex = new(
        @"\\frac\s*\{([^{}]*)\}\s*\{([^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex SqrtRegex = new(
        @"\\sqrt\s*\{([^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex SupBraceRegex = new(
        @"\^\s*\{([^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex SubBraceRegex = new(
        @"_\s*\{([^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex CommandWithBracesRegex = new(
        @"\\[a-zA-Z]+\s*\{([^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex BareCommandRegex = new(
        @"\\[a-zA-Z]+", RegexOptions.Compiled);

    private static readonly Regex MarkdownEmphasisRegex = new(
        @"\*\*|__|`", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+", RegexOptions.Compiled);

    private static readonly (string Pattern, string Replacement)[] LatexCommandMap =
    {
        (@"\\pi\b",        "pi"),
        (@"\\alpha\b",     "alpha"),
        (@"\\beta\b",      "beta"),
        (@"\\gamma\b",     "gamma"),
        (@"\\delta\b",     "delta"),
        (@"\\theta\b",     "theta"),
        (@"\\lambda\b",    "lambda"),
        (@"\\mu\b",        "mu"),
        (@"\\sigma\b",     "sigma"),
        (@"\\phi\b",       "phi"),
        (@"\\omega\b",     "omega"),
        (@"\\infty\b",     "inf"),
        (@"\\times\b",     "*"),
        (@"\\cdot\b",      "*"),
        (@"\\div\b",       "/"),
        (@"\\pm\b",        "+/-"),
        (@"\\leq\b",       "<="),
        (@"\\geq\b",       ">="),
        (@"\\neq\b",       "!="),
        (@"\\approx\b",    "~"),
        (@"\\to\b",        "->"),
        (@"\\rightarrow\b","->"),
        (@"\\leftarrow\b", "<-"),
    };

    private static readonly (string From, string To)[] UnicodeMap =
    {
        ("π", "pi"), ("α", "alpha"), ("β", "beta"), ("γ", "gamma"),
        ("δ", "delta"), ("θ", "theta"), ("λ", "lambda"), ("μ", "mu"),
        ("σ", "sigma"), ("φ", "phi"), ("ω", "omega"),
        ("∞", "inf"), ("×", "*"), ("÷", "/"), ("·", "*"),
        ("≤", "<="), ("≥", ">="), ("≠", "!="), ("≈", "~"),
        ("→", "->"), ("←", "<-"),
        ("²", "^2"), ("³", "^3"), ("⁰", "^0"), ("¹", "^1"),
        ("⁴", "^4"), ("⁵", "^5"), ("⁶", "^6"), ("⁷", "^7"),
        ("⁸", "^8"), ("⁹", "^9"),
        ("½", "1/2"), ("¼", "1/4"), ("¾", "3/4"), ("⅓", "1/3"), ("⅔", "2/3"),
    };

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var s = input;

        // 1) Matematik sınırlayıcılarını sil: $...$, $$...$$, \(...\), \[...\]
        s = MathDelimiterRegex.Replace(s, "");

        // 2) \frac{a}{b} → (a)/(b)  ve  \sqrt{a} → sqrt(a)
        s = FracRegex.Replace(s, "($1)/($2)");
        s = SqrtRegex.Replace(s, "sqrt($1)");

        // 3) Üs/alt-indis: x^{2} → x^2, x_{i} → x_i
        s = SupBraceRegex.Replace(s, "^$1");
        s = SubBraceRegex.Replace(s, "_$1");

        // 4) Yunan harfleri & operatör komutları (kelime sınırı kontrollü)
        foreach (var (pattern, replacement) in LatexCommandMap)
            s = Regex.Replace(s, pattern, replacement);

        // 5) Geri kalan \cmd{...} → içeriği bırak (\text{x} → x)
        s = CommandWithBracesRegex.Replace(s, "$1");

        // 6) Tek başına \cmd → sil (zarar veren yok, KaTeX-only komutlar düşer)
        s = BareCommandRegex.Replace(s, "");

        // 7) Unicode → ASCII eşdeğerleri
        foreach (var (from, to) in UnicodeMap)
            s = s.Replace(from, to);

        // 8) Markdown vurguları
        s = MarkdownEmphasisRegex.Replace(s, "");

        // 9) Süslü parantezleri at (geri kalan tek başınalar)
        s = s.Replace("{", "").Replace("}", "");

        // 10) Boşlukları sıkıştır + lowercase
        s = WhitespaceRegex.Replace(s, " ").Trim();

        return s.ToLowerInvariant();
    }
}
