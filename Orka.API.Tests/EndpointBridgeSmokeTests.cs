using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Orka.API.Tests;

public sealed class EndpointBridgeSmokeTests : IClassFixture<ApiSmokeFactory>
{
    private readonly HttpClient _client;

    public EndpointBridgeSmokeTests(ApiSmokeFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CoreLearningEndpoints_RoundTripWithoutAiProviderCalls()
    {
        var token = await RegisterAndLoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var topicId = await CreateTopicAsync();

        var wiki = await _client.GetAsync($"/api/wiki/{topicId}");
        wiki.EnsureSuccessStatusCode();

        var sources = await _client.GetAsync($"/api/sources/topic/{topicId}");
        sources.EnsureSuccessStatusCode();

        var quizRunId = Guid.NewGuid();
        var quiz = await _client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            messageId = "smoke-quiz",
            quizRunId,
            topicId,
            questionId = "q-fractions-1",
            question = "1/2 + 1/4 işlemini model kurarak çöz.",
            selectedOptionId = "A) 2/6",
            isCorrect = false,
            explanation = "Payda eşitleme eksik.",
            skillTag = "kesirlerde-payda-esitleme",
            topicPath = "Matematik > Kesirler > Payda eşitleme",
            difficulty = "orta",
            cognitiveType = "uygulama",
            questionHash = "smoke-fractions-001"
        });
        quiz.EnsureSuccessStatusCode();

        using var learningJson = await JsonDocument.ParseAsync(
            await (await _client.GetAsync($"/api/learning/topic/{topicId}/summary")).Content.ReadAsStreamAsync());
        Assert.True(learningJson.RootElement.GetProperty("totalAttempts").GetInt32() >= 1);
        Assert.Contains(
            learningJson.RootElement.GetProperty("weakSkills").EnumerateArray(),
            item => item.GetProperty("skillTag").GetString() == "kesirlerde-payda-esitleme");

        var history = await _client.GetStringAsync($"/api/quiz/history/{topicId}");
        Assert.Contains("smoke-fractions-001", history);

        var signal = await _client.PostAsJsonAsync("/api/learning/signal", new
        {
            topicId,
            signalType = "WikiActionClicked",
            payloadJson = "{\"surface\":\"smoke\"}"
        });
        signal.EnsureSuccessStatusCode();

        var dashboard = await _client.GetStringAsync("/api/dashboard/stats");
        Assert.Contains("learningSignalBook", dashboard);
        Assert.Contains("kesirlerde-payda-esitleme", dashboard);

        var classroom = await _client.PostAsJsonAsync("/api/classroom/session", new
        {
            topicId,
            transcript = "[HOCA]: Kesirlerde payda eşitlemeyi konuşuyoruz.\n[ASISTAN]: Bir örnekle pekiştirelim."
        });
        classroom.EnsureSuccessStatusCode();

        using var classroomJson = await JsonDocument.ParseAsync(await classroom.Content.ReadAsStreamAsync());
        var classroomId = classroomJson.RootElement.GetProperty("id").GetGuid();
        var classroomAsk = await _client.PostAsJsonAsync($"/api/classroom/{classroomId}/ask", new
        {
            question = "Bu kısmı anlamadım, daha basit anlatır mısın?",
            activeSegment = "[HOCA]: Paydaları eşitlemeden toplama yapamayız."
        });
        classroomAsk.EnsureSuccessStatusCode();
        var classroomAnswer = await classroomAsk.Content.ReadAsStringAsync();
        Assert.Contains("[HOCA]:", classroomAnswer);
        Assert.Contains("[ASISTAN]:", classroomAnswer);

        var codeRun = await _client.PostAsJsonAsync("/api/code/run", new
        {
            topicId,
            code = "Console.WriteLine(\"Smoke output\");",
            language = "csharp"
        });
        codeRun.EnsureSuccessStatusCode();
        var codeRunJson = await codeRun.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(codeRunJson.GetProperty("success").GetBoolean());

        var learningAfterCode = await _client.GetStringAsync($"/api/learning/topic/{topicId}/summary");
        Assert.Contains("IdeRunCompleted", learningAfterCode);

        var invalidCode = await _client.PostAsJsonAsync("/api/code/run", new
        {
            topicId,
            code = "",
            language = "csharp"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCode.StatusCode);
    }

    [Fact]
    public async Task SwaggerJson_ContainsCoreBridgeEndpoints()
    {
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json");

        string[] expected =
        [
            "/api/chat/message",
            "/api/wiki/{topicId}",
            "/api/sources/upload",
            "/api/sources/topic/{topicId}",
            "/api/quiz/attempt",
            "/api/learning/topic/{topicId}/summary",
            "/api/learning/signal",
            "/api/classroom/session",
            "/api/code/run"
        ];

        foreach (var path in expected)
        {
            Assert.Contains(path, swagger);
        }
    }

    [Fact]
    public async Task WikiV2Chat_ReturnsSafeGroundedSseAndWorkspaceState()
    {
        var token = await RegisterAndLoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var topicId = await CreateTopicAsync();

        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent(topicId.ToString()), "TopicId");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Payda eşitleme, kesir toplarken ortak payda bulma işlemidir."));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        upload.Add(file, "File", "kesirler.txt");

        var uploaded = await _client.PostAsync("/api/sources/upload", upload);
        uploaded.EnsureSuccessStatusCode();

        var workspace = await _client.GetStringAsync($"/api/wiki/{topicId}/workspace-state");
        Assert.Contains("readySourceCount", workspace);
        Assert.Contains("activeConcepts", workspace);

        var response = await _client.PostAsJsonAsync($"/api/wiki/{topicId}/chat", new
        {
            question = "Payda eşitleme nedir?"
        });
        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"type\":\"token\"", sse);
        Assert.Contains("[doc:", sse);
        Assert.Contains("\"type\":\"citation\"", sse);
        Assert.Contains("\"type\":\"metadata\"", sse);
        Assert.Contains("\"groundingStatus\":\"source_grounded\"", sse);
    }

    [Fact]
    public async Task WikiV2Chat_SourceMissingDoesNotInventAnswer()
    {
        var token = await RegisterAndLoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var topicId = await CreateTopicAsync();

        var response = await _client.PostAsJsonAsync($"/api/wiki/{topicId}/chat", new
        {
            question = "Bu kaynakta olmayan özel detayı açıkla."
        });
        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();

        Assert.Contains("mevcut kaynaklarda", sse);
        Assert.Contains("source_retrieval_empty", sse);
        Assert.Contains("\"groundingStatus\":\"no_source\"", sse);
        Assert.DoesNotContain("IWikiAgent", sse);
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"bridge-{Guid.NewGuid():N}@orka.local";
        var password = "BridgePass123!";

        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Bridge",
            lastName = "Smoke",
            email,
            password
        });
        register.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        using var loginJson = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        return loginJson.RootElement.GetProperty("token").GetString()
               ?? throw new InvalidOperationException("Login token missing.");
    }

    private async Task<Guid> CreateTopicAsync()
    {
        var topic = await _client.PostAsJsonAsync("/api/topics", new
        {
            title = "Smoke Matematik",
            emoji = "M",
            category = "Plan"
        });
        topic.EnsureSuccessStatusCode();

        using var topicJson = await JsonDocument.ParseAsync(await topic.Content.ReadAsStreamAsync());
        return topicJson.RootElement.GetProperty("id").GetGuid();
    }
}

