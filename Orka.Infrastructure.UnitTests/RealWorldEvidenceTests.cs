using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class RealWorldEvidenceTests
{
    [Fact]
    public void EvidenceRouter_SelectsDomainEvidenceWithoutDomainTemplates()
    {
        var router = new TeachingEvidenceRouter();
        var state = new TutorTurnStateDto
        {
            UserId = Guid.NewGuid(),
            UserMessage = "Turkiye nufus ve cografya bilgisini gercek verilerle anlat",
            ActiveConceptKey = "turkiye-cografya",
            ActiveConceptLabel = "Turkiye cografyasi",
            StyleMode = "example_first",
            MasteryProbability = 0.40m
        };

        var plans = router.Route(state, state.UserMessage.ToLowerInvariant());

        Assert.Contains(plans, p => p.ToolId == "geo_context");
        Assert.Contains(plans, p => p.ToolId == "socioeconomic_context");
        Assert.All(plans, p => Assert.NotEqual("weather", p.ToolId));
    }

    [Fact]
    public void EvidenceRouter_UsesForumOnlyForPracticalConfusionSignals()
    {
        var router = new TeachingEvidenceRouter();
        var state = new TutorTurnStateDto
        {
            UserId = Guid.NewGuid(),
            UserMessage = "Java NullReferenceException forumlarda insanlar nerede takiliyor?",
            ActiveConceptKey = "java-null-reference",
            ActiveConceptLabel = "Java null reference",
            StyleMode = "code_first"
        };

        var plans = router.Route(state, state.UserMessage.ToLowerInvariant());

        var forum = Assert.Single(plans.Where(p => p.ToolId == "forum_signal"));
        Assert.False(forum.Required);
        Assert.Equal("medium", forum.RiskLevel);
    }

    [Fact]
    public async Task ForumSignal_IsPersistedAsSignalNotFactualAuthority()
    {
        await using var db = CreateDb();
        var service = new RealWorldEvidenceService(
            new FakeHttpClientFactory(),
            db,
            NullLogger<RealWorldEvidenceService>.Instance,
            null);

        var userId = Guid.NewGuid();
        var result = await service.GetEvidenceAsync(new TeachingEvidenceRequestDto
        {
            UserId = userId,
            EvidenceType = "forum_signal",
            Query = "NullReferenceException",
            ConceptKey = "null-reference"
        });

        Assert.True(result.Success);
        Assert.Equal("ready", result.Status);
        Assert.NotEmpty(result.Cards);
        Assert.All(result.Cards, card =>
        {
            Assert.Equal("forum_signal", card.EvidenceType);
            Assert.Equal("medium", card.RiskLevel);
            Assert.True(
                card.ClassroomUse.Contains("not", StringComparison.OrdinalIgnoreCase) ||
                card.ClassroomUse.Contains("never", StringComparison.OrdinalIgnoreCase));
        });
        Assert.Equal(result.Cards.Count, await db.TeachingEvidenceItems.CountAsync());
        Assert.Equal(1, await db.TeachingEvidenceProviderHealth.CountAsync());
    }

    [Fact]
    public async Task MissingEvidenceQuery_DegradesWithoutInventingData()
    {
        await using var db = CreateDb();
        var service = new RealWorldEvidenceService(
            new FakeHttpClientFactory(),
            db,
            NullLogger<RealWorldEvidenceService>.Instance,
            null);

        var result = await service.GetEvidenceAsync(new TeachingEvidenceRequestDto
        {
            UserId = Guid.NewGuid(),
            EvidenceType = "knowledge_entity",
            Query = ""
        });

        Assert.False(result.Success);
        Assert.Equal("needs_input", result.Status);
        Assert.Empty(result.Cards);
        Assert.Contains("missing", result.ErrorCode);
    }

    private static OrkaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase($"real-world-evidence-{Guid.NewGuid():N}")
            .Options;
        return new OrkaDbContext(options);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler())
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var json = url.Contains("api.stackexchange.com", StringComparison.OrdinalIgnoreCase)
                ? """
                  {"items":[{"title":"What is a NullReferenceException, and how do I fix it?","link":"https://stackoverflow.com/questions/4660142","score":900}]}
                  """
                : url.Contains("hn.algolia.com", StringComparison.OrdinalIgnoreCase)
                    ? """
                      {"hits":[{"title":"Null references in real systems","url":"https://news.ycombinator.com/item?id=1"}]}
                      """
                    : "{}";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
