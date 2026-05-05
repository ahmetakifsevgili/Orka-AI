using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public class AudioDialogueFormatterTests
{
    [Fact]
    public void NormalizeScript_DefaultsPlainTextToHoca()
    {
        var script = AudioDialogueFormatter.NormalizeScript("Async await kisa anlatim.");

        Assert.StartsWith("[HOCA]:", script);
        Assert.Contains("Async await kisa anlatim.", script);
    }

    [Fact]
    public void ParseSpeakers_NormalizesTurkishAndEnglishAliases()
    {
        var script = """
        [ASİSTAN]: Bir soru sorayim.
        [Teacher]: Cevaplayalim.
        [Guest]: Dis bakis ekleyelim.
        """;

        var speakers = AudioDialogueFormatter.ParseSpeakers(script);

        Assert.Equal(["ASISTAN", "HOCA", "KONUK"], speakers);
    }

    [Fact]
    public void ParseSegments_ExtractsSpeakerTextBlocks()
    {
        var script = """
        [HOCA]: Ilk bolum.
        [ASISTAN]: Ikinci bolum.
        [KONUK]: Ucuncu bolum.
        """;

        var segments = AudioDialogueFormatter.ParseSegments(script);

        Assert.Equal(3, segments.Count);
        Assert.Equal(("HOCA", "Ilk bolum."), segments[0]);
        Assert.Equal(("ASISTAN", "Ikinci bolum."), segments[1]);
        Assert.Equal(("KONUK", "Ucuncu bolum."), segments[2]);
    }

    [Fact]
    public void ParseSegments_EmptyScriptReturnsSafeHocaSegment()
    {
        var segments = AudioDialogueFormatter.ParseSegments("");

        var segment = Assert.Single(segments);
        Assert.Equal("HOCA", segment.Speaker);
        Assert.False(string.IsNullOrWhiteSpace(segment.Text));
    }
}
