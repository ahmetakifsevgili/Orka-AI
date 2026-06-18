using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class UserOnboardingTests
{
    [Fact]
    public async Task Register_ReturnsOnboardingIncompleteForNewUsers()
    {
        using var factory = new ApiSmokeFactory();
        var user = await RegisterAsync(factory);

        Assert.False(user.IsOnboardingCompleted);

        using var response = await user.Client.GetAsync("/api/user/me");
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.False(body.RootElement.GetProperty("isOnboardingCompleted").GetBoolean());
    }

    [Fact]
    public async Task SaveOnboarding_CompletesUserWithoutDeletingPlanDiagnostics()
    {
        using var factory = new ApiSmokeFactory();
        var user = await RegisterAsync(factory);
        var planProfileId = Guid.NewGuid();
        var planRequestId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.DiagnosticProfiles.Add(new DiagnosticProfile
            {
                Id = planProfileId,
                UserId = user.UserId,
                PlanRequestId = planRequestId,
                AnsweredCount = 12,
                CorrectCount = 9,
                AccuracyPercent = 75,
                MeasuredLevel = "intermediate",
                ProfileJson = """{"kind":"plan-diagnostic"}""",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        }

        var first = await user.Client.PostAsJsonAsync("/api/user/onboarding", new
        {
            answeredCount = 5,
            correctCount = 4,
            measuredLevel = "developing",
            learningStyle = "practical",
            pathPreference = "fast",
            theme = "Light"
        });
        first.EnsureSuccessStatusCode();
        using (var body = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync()))
        {
            Assert.True(body.RootElement.GetProperty("isOnboardingCompleted").GetBoolean());
        }

        var second = await user.Client.PostAsJsonAsync("/api/user/onboarding", new
        {
            answeredCount = 8,
            correctCount = 6,
            measuredLevel = "intermediate",
            learningStyle = "theoretical",
            pathPreference = "standard"
        });
        second.EnsureSuccessStatusCode();

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var savedUser = await verifyDb.Users.SingleAsync(u => u.Id == user.UserId);
        var profiles = await verifyDb.DiagnosticProfiles
            .Where(p => p.UserId == user.UserId)
            .ToListAsync();

        Assert.True(savedUser.IsOnboardingCompleted);
        Assert.Equal("Light", savedUser.Theme);
        Assert.Contains(profiles, p => p.Id == planProfileId && p.PlanRequestId == planRequestId);

        var onboardingProfile = Assert.Single(profiles.Where(p =>
            p.TopicId == null &&
            p.QuizRunId == null &&
            p.PlanRequestId == null &&
            p.ConceptGraphSnapshotId == null));

        Assert.Equal(8, onboardingProfile.AnsweredCount);
        Assert.Equal(6, onboardingProfile.CorrectCount);
        Assert.Equal(75, onboardingProfile.AccuracyPercent);
        Assert.Equal("intermediate", onboardingProfile.MeasuredLevel);
        Assert.Equal(2, profiles.Count);
    }

    private static async Task<TestUser> RegisterAsync(ApiSmokeFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"onboarding-{Guid.NewGuid():N}@orka.local";
        const string password = "OnboardingPass123!";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Onboarding",
            lastName = "User",
            email,
            password
        });
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = body.RootElement.GetProperty("token").GetString()
                    ?? throw new InvalidOperationException("Register token missing.");
        var userElement = body.RootElement.GetProperty("user");
        var userId = Guid.Parse(userElement.GetProperty("id").GetString()!);
        var isOnboardingCompleted = userElement.GetProperty("isOnboardingCompleted").GetBoolean();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestUser(client, userId, isOnboardingCompleted);
    }

    private sealed record TestUser(HttpClient Client, Guid UserId, bool IsOnboardingCompleted);
}
