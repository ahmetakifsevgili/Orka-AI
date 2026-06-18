using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Orka.API.Services;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task TenantFilters_hide_other_tenant_rows_and_required_dependents()
    {
        var options = CreateOptions();
        var tenantA = "tenant-a";
        var tenantB = "tenant-b";

        var userA = CreateUser("tenant-a@example.test");
        var userB = CreateUser("tenant-b@example.test");
        var topicA = CreateTopic(userA.Id, tenantA, "Tenant A topic");
        var topicB = CreateTopic(userB.Id, tenantB, "Tenant B topic");
        var sessionA = CreateSession(userA.Id, topicA.Id, tenantA);
        var sessionB = CreateSession(userB.Id, topicB.Id, tenantB);
        var messageA = CreateMessage(userA.Id, sessionA.Id, tenantA, "A");
        var messageB = CreateMessage(userB.Id, sessionB.Id, tenantB, "B");

        await using (var seed = new OrkaDbContext(options))
        {
            seed.Users.AddRange(userA, userB);
            seed.Topics.AddRange(topicA, topicB);
            seed.Sessions.AddRange(sessionA, sessionB);
            seed.Messages.AddRange(messageA, messageB);
            seed.AgentEvaluations.AddRange(
                CreateAgentEvaluation(userA.Id, sessionA.Id, messageA.Id),
                CreateAgentEvaluation(userB.Id, sessionB.Id, messageB.Id));
            await seed.SaveChangesAsync();
        }

        await using (var scoped = new OrkaDbContext(options, new FixedTenantService(tenantA)))
        {
            var visibleTopic = Assert.Single(await scoped.Topics.ToListAsync());
            Assert.Equal(topicA.Id, visibleTopic.Id);

            var visibleEvaluation = Assert.Single(await scoped.AgentEvaluations.ToListAsync());
            Assert.Equal(messageA.Id, visibleEvaluation.MessageId);
        }

        await using (var bypassed = new OrkaDbContext(options))
        {
            Assert.Equal(2, await bypassed.Topics.CountAsync());
            Assert.Equal(2, await bypassed.AgentEvaluations.CountAsync());
        }
    }

    [Fact]
    public async Task SaveChanges_sets_missing_tenant_and_rejects_cross_tenant_writes()
    {
        var options = CreateOptions();
        var tenantA = "tenant-a";
        var user = CreateUser("writer@example.test");

        await using (var scoped = new OrkaDbContext(options, new FixedTenantService(tenantA)))
        {
            scoped.Users.Add(user);
            scoped.Topics.Add(CreateTopic(user.Id, string.Empty, "Assigned tenant"));
            await scoped.SaveChangesAsync();
        }

        await using (var scoped = new OrkaDbContext(options, new FixedTenantService(tenantA)))
        {
            var saved = Assert.Single(await scoped.Topics.ToListAsync());
            Assert.Equal(tenantA, saved.TenantId);
        }

        await using (var scoped = new OrkaDbContext(options, new FixedTenantService(tenantA)))
        {
            scoped.Topics.Add(CreateTopic(user.Id, tenantId: "tenant-b", "Rejected tenant"));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.SaveChangesAsync());
            Assert.Contains("different tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TenantService_UsesBackgroundUserContextWhenHttpContextIsMissing()
    {
        var context = new AsyncLocalAiRequestContextAccessor();
        var userId = Guid.NewGuid();

        using var _ = context.Push(new AiRequestContext(UserId: userId, IsBackground: true));
        var tenant = new TenantService(new HttpContextAccessor(), context);

        Assert.False(tenant.BypassTenantFilters);
        Assert.Equal($"user:{userId}", tenant.GetCurrentTenantId());
    }

    [Fact]
    public void TenantService_BypassesTenantFiltersOnlyForRootScope()
    {
        var tenant = new TenantService(new HttpContextAccessor(), new AsyncLocalAiRequestContextAccessor());

        Assert.True(tenant.BypassTenantFilters);
        Assert.Equal(string.Empty, tenant.GetCurrentTenantId());
    }

    [Fact]
    public async Task RequestlessRootScope_BackfillsTenantFromUserIdWhenWritingTenantEntity()
    {
        var options = CreateOptions();
        var user = CreateUser("root-writer@example.test");

        await using var db = new OrkaDbContext(options);
        db.Users.Add(user);
        db.Topics.Add(CreateTopic(user.Id, string.Empty, "Root backfilled"));
        await db.SaveChangesAsync();

        var saved = Assert.Single(await db.Topics.ToListAsync());
        Assert.Equal($"user:{user.Id}", saved.TenantId);
    }

    [Fact]
    public void AesEncryptionHelper_KeylessModeDoesNotPretendToEncrypt()
    {
        AesEncryptionHelper.ConfigureKey(null);

        Assert.False(AesEncryptionHelper.IsConfigured);
        Assert.Equal("plain text", AesEncryptionHelper.Encrypt("plain text"));
        Assert.Equal("plain text", AesEncryptionHelper.Decrypt("plain text"));
    }

    [Fact]
    public void AesEncryptionHelper_ConfiguredKeyEncryptsAndDecrypts()
    {
        try
        {
            AesEncryptionHelper.ConfigureKey("ORKA_TEST_DATABASE_ENCRYPTION_KEY_64_CHARS_2026_01");

            var encrypted = AesEncryptionHelper.Encrypt("secret lesson content");

            Assert.True(AesEncryptionHelper.IsConfigured);
            Assert.NotEqual("secret lesson content", encrypted);
            Assert.Equal("secret lesson content", AesEncryptionHelper.Decrypt(encrypted));
        }
        finally
        {
            AesEncryptionHelper.ConfigureKey(null);
        }
    }

    [Fact]
    public async Task CurrentUserDtoFactory_includes_onboarding_and_settings_contract()
    {
        var options = CreateOptions();
        var user = CreateUser("profile@example.test", theme: "Light");

        await using var db = new OrkaDbContext(options);
        db.Users.Add(user);
        db.DiagnosticProfiles.Add(new DiagnosticProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AnsweredCount = 1,
            CorrectCount = 1,
            AccuracyPercent = 100,
            MeasuredLevel = "Advanced",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Limits:FreeUserDailyMessages"] = "77"
            })
            .Build();

        var dto = await CurrentUserDtoFactory.CreateAsync(user, db, configuration);

        Assert.True(dto.IsOnboardingCompleted);
        Assert.Equal(77, dto.DailyLimit);
        Assert.Equal("Light", dto.Settings.Theme);
        Assert.Equal(user.StorageLimitMB, dto.StorageLimitMB);
    }

    private static DbContextOptions<OrkaDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase($"tenant-isolation-{Guid.NewGuid():N}")
            .Options;

    private static User CreateUser(string email, string theme = "Dark") => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        FirstName = "Test",
        LastName = "User",
        PasswordHash = "hash",
        StorageLimitMB = 100,
        DailyMessageResetAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        Theme = theme
    };

    private static Topic CreateTopic(Guid userId, string tenantId, string title) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        Title = title,
        CreatedAt = DateTime.UtcNow,
        LastAccessedAt = DateTime.UtcNow
    };

    private static Session CreateSession(Guid userId, Guid topicId, string tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        TopicId = topicId,
        CreatedAt = DateTime.UtcNow
    };

    private static Message CreateMessage(Guid userId, Guid sessionId, string tenantId, string content) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        SessionId = sessionId,
        Role = "assistant",
        Content = content,
        CreatedAt = DateTime.UtcNow
    };

    private static AgentEvaluation CreateAgentEvaluation(Guid userId, Guid sessionId, Guid messageId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        SessionId = sessionId,
        MessageId = messageId,
        AgentRole = "Tutor",
        UserInput = "input",
        AgentResponse = "response",
        EvaluationScore = 8,
        EvaluatorFeedback = "ok",
        CreatedAt = DateTime.UtcNow
    };

    private sealed class FixedTenantService(string tenantId) : ITenantService
    {
        public bool BypassTenantFilters => false;

        public string GetCurrentTenantId() => tenantId;
    }
}
