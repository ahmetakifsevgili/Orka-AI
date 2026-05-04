using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public class ReviewIdentitySelectorTests
{
    [Fact]
    public void Select_PrefersConceptTagOverSkill()
    {
        var identity = ReviewIdentitySelector.Select(
            conceptTag: "async-deadlock",
            skillTag: "csharp-async",
            learningObjective: "Avoid deadlocks",
            topicPath: "C# > Async");

        Assert.Equal("async-deadlock", identity);
    }

    [Fact]
    public void Select_FallsBackToSkillThenObjectiveThenPath()
    {
        Assert.Equal("csharp-async", ReviewIdentitySelector.Select(null, "csharp-async", "objective", "path"));
        Assert.Equal("objective", ReviewIdentitySelector.Select(null, null, "objective", "path"));
        Assert.Equal("path", ReviewIdentitySelector.Select(null, null, null, "path"));
    }

    [Fact]
    public void Select_IsNullSafeAndCapped()
    {
        var longConcept = new string('x', 160);

        var capped = ReviewIdentitySelector.Select(longConcept, null, null, null);
        var fallback = ReviewIdentitySelector.Select(null, null, null, null);

        Assert.Equal(120, capped.Length);
        Assert.Equal("unknown skill", fallback);
    }
}
