using System.Collections;
using System.Reflection;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class PlanQualityGuardTests
{
    [Theory]
    [InlineData("DetectPlanDomain")]
    [InlineData("BuildDomainPlanningGuidance")]
    [InlineData("BuildDomainFallbackModules")]
    public void DeepPlan_NoLongerExposesDomainSpecificPlanningMode(string methodName)
    {
        var method = typeof(DeepPlanAgent).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.Null(method);
    }

    [Fact]
    public void DeepPlan_GuidanceIsConceptGraphBased()
    {
        var guidance = Assert.IsType<string>(InvokePrivateStatic("BuildConceptGraphPlanningGuidance"));

        Assert.Contains("GENERIC CONCEPT GRAPH", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("onkosul", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ana kavram", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mastery", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SINAV HAZIRLIK", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALGORITMA / HACKERRANK", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Spaced repetition", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("KPSS genel yetenek ve genel kultur")]
    [InlineData("HackerRank algoritma hazirligi")]
    [InlineData("Matematik olasilik ve kombinasyon")]
    [InlineData("IELTS icin Ingilizce konusma")]
    public void DeepPlan_FallbackModulesComeFromGenericConceptGraph(string topic)
    {
        var modules = GetFallbackModules(topic);
        var text = string.Join("\n", modules.SelectMany(m => new[] { m.Title }.Concat(m.Topics)));

        Assert.True(modules.Count >= 6);
        Assert.All(modules, module => Assert.True(module.Topics.Count >= 4));
        Assert.True(modules.Sum(module => module.Topics.Count) >= 24);
        Assert.Contains("Onkosul", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ana Kavram", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Yanilgi Onarimi", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mastery Kontrolu", text, StringComparison.OrdinalIgnoreCase);

        string[] forbidden =
        [
            "PlanDomain",
            "BuildDomainFallbackModules",
            "Two Pointers",
            "Dynamic Programming",
            "Spaced Repetition",
            "Speaking Prompt",
            "Genel Bakis",
            "Modul 1",
            "Bolum 1"
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
        var result = InvokePrivateStatic("BuildQualityFallbackModules", topic);
        Assert.NotNull(result);

        var output = new List<(string Title, IReadOnlyList<string> Topics)>();
        foreach (var item in ((IEnumerable)result!).Cast<object>())
        {
            var type = item.GetType();
            var title = type.GetProperty("Title")?.GetValue(item)?.ToString() ?? string.Empty;
            var topicsRaw = type.GetProperty("Topics")?.GetValue(item) as IEnumerable;
            var topics = topicsRaw?.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToList();
            if (topics is null)
            {
                var lessonsRaw = type.GetProperty("Lessons")?.GetValue(item) as IEnumerable;
                topics = lessonsRaw?.Cast<object>().Select(lesson =>
                {
                    var lessonType = lesson.GetType();
                    return lessonType.GetProperty("Title")?.GetValue(lesson)?.ToString() ?? string.Empty;
                }).ToList() ?? [];
            }

            output.Add((title, topics));
        }

        return output;
    }
}
