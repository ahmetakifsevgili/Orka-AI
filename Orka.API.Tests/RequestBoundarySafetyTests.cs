using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class RequestBoundarySafetyTests
{
    [Fact]
    public async Task CodeRun_WithCrossUserTopicId_ReturnsNotFoundAndDoesNotRecordSignal()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var (topicId, _) = await CreateTopicSessionAsync(factory, userB.UserId);

        var response = await userA.Client.PostAsJsonAsync("/api/code/run", new
        {
            code = "Console.WriteLine(\"ok\");",
            language = "csharp",
            topicId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.False(await db.LearningSignals.AnyAsync(s => s.UserId == userA.UserId && s.TopicId == topicId));
    }

    [Fact]
    public async Task CodeRun_WithCrossUserSessionId_ReturnsNotFound()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var (_, sessionId) = await CreateTopicSessionAsync(factory, userB.UserId);

        var response = await userA.Client.PostAsJsonAsync("/api/code/run", new
        {
            code = "Console.WriteLine(\"ok\");",
            language = "csharp",
            sessionId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SourceOperations_WithCrossUserSourceId_ReturnNotFound()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var (_, _, sourceId) = await CreateTopicSessionSourceAsync(factory, userB.UserId);

        var ask = await userA.Client.PostAsJsonAsync($"/api/sources/{sourceId}/ask", new { question = "Bu kaynak ne diyor?" });
        var page = await userA.Client.GetAsync($"/api/sources/{sourceId}/pages/1");
        var update = await userA.Client.PatchAsJsonAsync($"/api/sources/{sourceId}", new { title = "Yeni baslik" });
        var delete = await userA.Client.DeleteAsync($"/api/sources/{sourceId}");

        Assert.Equal(HttpStatusCode.NotFound, ask.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task SourceUpload_WithCrossUserTopicId_ReturnsNotFoundAndDoesNotCreateSource()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var (topicId, _) = await CreateTopicSessionAsync(factory, userB.UserId);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(topicId.ToString()), "TopicId");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("source text")), "File", "notes.txt");

        var response = await userA.Client.PostAsync("/api/sources/upload", form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.False(await db.LearningSources.AnyAsync(s => s.UserId == userA.UserId && s.TopicId == topicId));
    }

    [Fact]
    public async Task Chat_WithCrossUserSessionOrFocusTopic_ReturnsNotFound()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var (topicId, sessionId) = await CreateTopicSessionAsync(factory, userB.UserId);

        var crossSession = await userA.Client.PostAsJsonAsync("/api/chat/message", new
        {
            content = "Merhaba",
            sessionId
        });

        var crossFocus = await userA.Client.PostAsJsonAsync("/api/chat/message", new
        {
            content = "Merhaba",
            focusTopicId = topicId
        });

        Assert.Equal(HttpStatusCode.NotFound, crossSession.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossFocus.StatusCode);
    }

    [Fact]
    public async Task LearningSignal_WithCrossUserTopicOrSession_ReturnsNotFound()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var (topicId, sessionId) = await CreateTopicSessionAsync(factory, userB.UserId);

        var crossTopic = await userA.Client.PostAsJsonAsync("/api/learning/signal", new
        {
            topicId,
            signalType = "source_uploaded"
        });

        var crossSession = await userA.Client.PostAsJsonAsync("/api/learning/signal", new
        {
            sessionId,
            signalType = "source_uploaded"
        });

        Assert.Equal(HttpStatusCode.NotFound, crossTopic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossSession.StatusCode);
    }

    [Fact]
    public async Task MalformedPayloads_ReturnControlledBadRequest()
    {
        using var factory = new ApiSmokeFactory();
        var user = await RegisterAuthenticatedClientAsync(factory);

        var nullCode = await user.Client.PostAsync(
            "/api/code/run",
            new StringContent("null", Encoding.UTF8, "application/json"));
        var emptyCode = await user.Client.PostAsJsonAsync("/api/code/run", new { code = "", language = "csharp" });
        var invalidLanguage = await user.Client.PostAsJsonAsync("/api/code/run", new { code = "print(1)", language = "../../bad" });
        var emptyChat = await user.Client.PostAsJsonAsync("/api/chat/message", new { content = "" });
        var longChat = await user.Client.PostAsJsonAsync("/api/chat/message", new { content = new string('x', 20_001) });

        Assert.Equal(HttpStatusCode.BadRequest, nullCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLanguage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyChat.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longChat.StatusCode);
    }

    [Fact]
    public async Task InvalidPageScoreAndQuality_ReturnControlledBadRequest()
    {
        using var factory = new ApiSmokeFactory();
        var user = await RegisterAuthenticatedClientAsync(factory);
        var (_, _, sourceId) = await CreateTopicSessionSourceAsync(factory, user.UserId);

        var invalidPage = await user.Client.GetAsync($"/api/sources/{sourceId}/pages/0");
        var invalidScore = await user.Client.PostAsJsonAsync("/api/learning/signal", new
        {
            signalType = "source_uploaded",
            score = 101
        });
        var invalidQuality = await user.Client.PostAsJsonAsync(
            $"/api/learning/review/{Guid.NewGuid()}/complete",
            new { quality = 6 });

        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidScore.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidQuality.StatusCode);
    }

    private static async Task<TestUser> RegisterAuthenticatedClientAsync(ApiSmokeFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"boundary-{Guid.NewGuid():N}@orka.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Boundary",
            lastName = "User",
            email,
            password = "BoundaryPass123!"
        });
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = body.RootElement.GetProperty("token").GetString()
                    ?? throw new InvalidOperationException("Register token missing.");
        var userIdText = body.RootElement.GetProperty("user").GetProperty("id").GetString()
                         ?? body.RootElement.GetProperty("userId").GetString()
                         ?? throw new InvalidOperationException("Register user id missing.");
        var userId = Guid.Parse(userIdText);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestUser(client, userId);
    }

    private static async Task<(Guid TopicId, Guid SessionId)> CreateTopicSessionAsync(ApiSmokeFactory factory, Guid userId)
    {
        var (topicId, sessionId, _) = await CreateTopicSessionSourceAsync(factory, userId, includeSource: false);
        return (topicId, sessionId);
    }

    private static Task<(Guid TopicId, Guid SessionId, Guid SourceId)> CreateTopicSessionSourceAsync(ApiSmokeFactory factory, Guid userId) =>
        CreateTopicSessionSourceAsync(factory, userId, includeSource: true);

    private static async Task<(Guid TopicId, Guid SessionId, Guid SourceId)> CreateTopicSessionSourceAsync(
        ApiSmokeFactory factory,
        Guid userId,
        bool includeSource)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Boundary Topic",
            Category = "Genel",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topic.Id,
            SessionNumber = 1,
            CreatedAt = DateTime.UtcNow
        };

        db.Topics.Add(topic);
        db.Sessions.Add(session);

        var sourceId = Guid.Empty;
        if (includeSource)
        {
            sourceId = Guid.NewGuid();
            db.LearningSources.Add(new LearningSource
            {
                Id = sourceId,
                UserId = userId,
                TopicId = topic.Id,
                SessionId = session.Id,
                SourceType = "document",
                Title = "Boundary Source",
                FileName = "boundary.txt",
                ContentType = "text/plain",
                PageCount = 1,
                ChunkCount = 1,
                Status = "ready",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.SourceChunks.Add(new SourceChunk
            {
                Id = Guid.NewGuid(),
                LearningSourceId = sourceId,
                PageNumber = 1,
                ChunkIndex = 0,
                Text = "Boundary source text",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return (topic.Id, session.Id, sourceId);
    }

    private sealed record TestUser(HttpClient Client, Guid UserId);
}
