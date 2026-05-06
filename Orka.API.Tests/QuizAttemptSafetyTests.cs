using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orka.API.Tests;

public sealed class QuizAttemptSafetyTests : IClassFixture<ApiSmokeFactory>
{
    private readonly HttpClient _client;

    public QuizAttemptSafetyTests(ApiSmokeFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task QuizAttempt_InvalidQuizRunId_DoesNotReturnServerError()
    {
        var token = await RegisterAndGetTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quiz/attempt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            messageId = $"smoke-{Guid.NewGuid():N}",
            quizRunId = Guid.NewGuid(),
            questionId = "q1",
            question = "C# async akista await ne ise yarar?",
            selectedOptionId = "A) Async isi bloklamadan bekletir.",
            isCorrect = true,
            explanation = "Await, Task sonucunu akisi bloklamadan beklemek icin kullanilir.",
            skillTag = "async-await",
            topicPath = "CSharp/Async",
            difficulty = "kolay",
            cognitiveType = "conceptual",
            questionHash = $"invalid-run-{Guid.NewGuid():N}"
        });

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("quizRunId", out var quizRunId));
        Assert.Equal(JsonValueKind.Null, quizRunId.ValueKind);
    }

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = $"quiz-{Guid.NewGuid():N}@orka.local";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Quiz",
            lastName = "Smoke",
            email,
            password = "SmokePass123!"
        });
        register.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await register.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }
}
