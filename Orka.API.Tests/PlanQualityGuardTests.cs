using System.Collections;
using System.Reflection;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class PlanQualityGuardTests
{
    [Theory]
    [InlineData("KPSS genel yetenek ve genel kultur", "Exam")]
    [InlineData("HackerRank algoritma hazirligi", "Algorithm")]
    [InlineData("Matematik olasilik ve kombinasyon", "Math")]
    [InlineData("IELTS icin Ingilizce konusma", "Language")]
    public void DeepPlan_DetectsDomainSpecificPlanningMode(string topic, string expectedDomain)
    {
        var domain = InvokePrivateStatic("DetectPlanDomain", topic);

        Assert.Equal(expectedDomain, domain?.ToString());
    }

    [Theory]
    [InlineData("KPSS genel yetenek ve genel kultur", "SINAV HAZIRLIK", "Deneme", "Yanlis")]
    [InlineData("HackerRank algoritma hazirligi", "ALGORITMA / HACKERRANK", "two pointers", "IDE")]
    [InlineData("Matematik olasilik ve kombinasyon", "MATEMATIK", "Formulun", "Karma")]
    [InlineData("IELTS icin Ingilizce konusma", "DIL OGRENIMI", "Spaced repetition", "speaking prompt")]
    public void DeepPlan_GuidanceIsDomainSpecific(string topic, string requiredHeader, string requiredA, string requiredB)
    {
        var guidance = Assert.IsType<string>(InvokePrivateStatic("BuildDomainPlanningGuidance", topic));

        Assert.Contains(requiredHeader, guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requiredA, guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requiredB, guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("KPSS genel yetenek ve genel kultur", "Deneme", "Paragraf", "Matematik")]
    [InlineData("HackerRank algoritma hazirligi", "Two Pointers", "Dynamic Programming", "HackerRank")]
    [InlineData("Matematik olasilik ve kombinasyon", "Formulun", "Problem", "Telafi")]
    [InlineData("IELTS icin Ingilizce konusma", "Telaffuz", "Speaking", "Spaced Repetition")]
    public void DeepPlan_FallbackModulesAreNotGeneric(string topic, string requiredA, string requiredB, string requiredC)
    {
        var modules = GetFallbackModules(topic);
        var text = string.Join("\n", modules.SelectMany(m => new[] { m.Title }.Concat(m.Topics)));

        Assert.True(modules.Count >= 4);
        Assert.Contains(requiredA, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requiredB, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requiredC, text, StringComparison.OrdinalIgnoreCase);

        string[] forbidden =
        [
            "Genel Bakis",
            "Genel Bakış",
            "Modul 1",
            "Modül 1",
            "Bolum 1",
            "Bölüm 1"
        ];

        foreach (var phrase in forbidden)
        {
            Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(DeepPlanAgent).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return method!.Invoke(null, args);
    }

    private static IReadOnlyList<(string Title, IReadOnlyList<string> Topics)> GetFallbackModules(string topic)
    {
        var result = InvokePrivateStatic("BuildDomainFallbackModules", topic, false);
        Assert.NotNull(result);

        var output = new List<(string Title, IReadOnlyList<string> Topics)>();
        foreach (var item in ((IEnumerable)result!).Cast<object>())
        {
            var type = item.GetType();
            var title = type.GetProperty("Title")?.GetValue(item)?.ToString() ?? string.Empty;
            var topicsRaw = type.GetProperty("Topics")?.GetValue(item) as IEnumerable;
            var topics = topicsRaw?.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToList() ?? [];
            output.Add((title, topics));
        }

        return output;
    }
}
