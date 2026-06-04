using System.Text.Json.Nodes;
using Orka.Infrastructure.Utilities;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class PublicTextNormalizerTests
{
    [Fact]
    public void RepairMojibake_RepairsTurkishAndAsciiSymbols()
    {
        var dirty = "Cevap B se\u00c3\u00a7ene\u00c4\u009fi. \u00c3\u0096\u00c4\u009frenci \u00e2\u0086\u0092 mikro kontrol.";

        var repaired = PublicTextNormalizer.RepairMojibake(dirty);

        Assert.Equal("Cevap B se\u00e7ene\u011fi. \u00d6\u011frenci -> mikro kontrol.", repaired);
        Assert.DoesNotContain("\u00c3", repaired);
        Assert.DoesNotContain("\u00c4", repaired);
    }

    [Fact]
    public void RepairJsonStrings_RepairsNestedPublicText()
    {
        var node = JsonNode.Parse("""
        {
          "answer": "Kayna\u00c4\u009fa g\u00c3\u00b6re",
          "items": [
            { "label": "\u00c5\u009eimdi tekrar deneyelim mi?" }
          ]
        }
        """);

        PublicTextNormalizer.RepairJsonStrings(node);

        Assert.Equal("Kayna\u011fa g\u00f6re", node?["answer"]?.GetValue<string>());
        Assert.Equal("\u015eimdi tekrar deneyelim mi?", node?["items"]?[0]?["label"]?.GetValue<string>());
    }
}
