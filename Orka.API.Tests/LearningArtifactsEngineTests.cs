using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class LearningArtifactsEngineTests
{
    [Fact]
    public async Task CreateAndListArtifacts_ExposeSafeLifecycleContractOnly()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "artifacts-create");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Artifact Topic");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, topicId, DateTime.UtcNow);

        var create = await user.Client.PostAsJsonAsync("/api/learning-artifacts", new
        {
            topicId,
            sessionId,
            conceptKey = "fractions",
            conceptLabel = "Fractions",
            artifactType = "mermaid_diagram",
            artifactStatus = "ready",
            origin = "tutor",
            renderFormat = "mermaid",
            title = "Fractions relation diagram",
            safeContent = "graph TD\nA[Pay] --> B[Payda]",
            sourceBasis = "model_assisted",
            accessibility = new
            {
                caption = "Fractions relation diagram",
                summary = "A simple concept relation diagram",
                textFallback = "Pay and payda relation"
            }
        });

        create.EnsureSuccessStatusCode();
        var createdJson = await create.Content.ReadAsStringAsync();
        var artifact = JsonSerializer.Deserialize<LearningArtifactDto>(createdJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(artifact);
        Assert.Equal("mermaid_diagram", artifact!.ArtifactType);
        Assert.Equal("model_assisted", artifact.SourceBasis);
        Assert.DoesNotContain(user.UserId.ToString(), createdJson, StringComparison.OrdinalIgnoreCase);

        await user.Client.PostAsJsonAsync("/api/learning-artifacts", new
        {
            topicId,
            sessionId,
            conceptKey = "fractions",
            artifactType = "table",
            artifactStatus = "ready",
            origin = "plan",
            renderFormat = "json_table",
            title = "Fraction table",
            safeContent = "Pay | Payda",
            contentJson = "{\"columns\":[\"term\",\"meaning\"],\"rows\":[[\"pay\",\"top\"]]}",
            sourceBasis = "model_assisted",
            accessibility = new { caption = "Fraction term table", summary = "Fraction terms" }
        });

        var list = await user.Client.GetFromJsonAsync<LearningArtifactListDto>($"/api/learning-artifacts?topicId={topicId}&sessionId={sessionId}&conceptKey=fractions");
        Assert.NotNull(list);
        Assert.True(list!.Count >= 2);
        Assert.All(list.Items, item =>
        {
            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.DoesNotContain("rawProviderPayload", item.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\", item.SafeContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task SafetyAndAccessibilityValidation_BlockUnsafeAndWarnWeakArtifacts()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "artifacts-safety");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Artifact Safety");

        var unsafeResponse = await user.Client.PostAsJsonAsync("/api/learning-artifacts/validate", new
        {
            topicId,
            artifactType = "concept_summary",
            origin = "tutor",
            renderFormat = "markdown",
            title = "Unsafe",
            safeContent = "<script>alert(1)</script> C:\\secret\\raw.txt kesin basarirsin",
            sourceBasis = "model_assisted"
        });
        unsafeResponse.EnsureSuccessStatusCode();
        var unsafeResult = await unsafeResponse.Content.ReadFromJsonAsync<LearningArtifactSafetyDto>();
        Assert.NotNull(unsafeResult);
        Assert.Contains("unsafe_raw_or_executable_content", unsafeResult!.BlockingIssues);
        Assert.Contains("local_path_reference_blocked", unsafeResult.BlockingIssues);
        Assert.Contains("unsafe_learning_claim_or_workflow_copy", unsafeResult.BlockingIssues);

        var imageResponse = await user.Client.PostAsJsonAsync("/api/learning-artifacts/validate", new
        {
            topicId,
            artifactType = "image_reference",
            origin = "tutor",
            renderFormat = "media_reference",
            title = "Image placeholder",
            safeContent = "Image placeholder for learning only.",
            sourceBasis = "model_assisted"
        });
        imageResponse.EnsureSuccessStatusCode();
        var imageResult = await imageResponse.Content.ReadFromJsonAsync<LearningArtifactSafetyDto>();
        Assert.NotNull(imageResult);
        Assert.Contains("media_missing_alt_or_caption", imageResult!.Warnings);

        var formulaResponse = await user.Client.PostAsJsonAsync("/api/learning-artifacts/validate", new
        {
            topicId,
            artifactType = "formula",
            origin = "quiz",
            renderFormat = "formula_text",
            title = "Formula",
            safeContent = "",
            sourceBasis = "model_assisted"
        });
        formulaResponse.EnsureSuccessStatusCode();
        var formulaResult = await formulaResponse.Content.ReadFromJsonAsync<LearningArtifactSafetyDto>();
        Assert.NotNull(formulaResult);
        Assert.Contains("missing_safe_content", formulaResult!.BlockingIssues);
        Assert.Contains("formula_requires_text_fallback", formulaResult.BlockingIssues);
    }

    [Fact]
    public async Task SourceGroundedArtifactRequiresEvidenceAndDegradesWhenEvidenceIsDeleted()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "artifacts-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Artifact Source");

        var denied = await user.Client.PostAsJsonAsync("/api/learning-artifacts", new
        {
            topicId,
            artifactType = "source_excerpt_summary",
            artifactStatus = "ready",
            origin = "source",
            renderFormat = "markdown",
            title = "Source claim",
            safeContent = "This is source grounded.",
            sourceBasis = "source_grounded"
        });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

        Guid bundleId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var bundle = new SourceEvidenceBundle
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                BundleHash = "artifact-source-bundle",
                EvidenceStatus = "source_grounded",
                SourceCount = 1,
                ReadySourceCount = 1,
                ChunkCount = 1,
                CitationCoverage = 1m,
                EvidenceJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            bundleId = bundle.Id;
            db.SourceEvidenceBundles.Add(bundle);
            await db.SaveChangesAsync();
        }

        var created = await user.Client.PostAsJsonAsync("/api/learning-artifacts", new
        {
            topicId,
            sourceEvidenceBundleId = bundleId,
            artifactType = "source_excerpt_summary",
            artifactStatus = "ready",
            origin = "source",
            renderFormat = "markdown",
            title = "Source card",
            safeContent = "Bounded source summary.",
            sourceBasis = "source_grounded",
            accessibility = new { summary = "Bounded source summary" }
        });
        created.EnsureSuccessStatusCode();
        var artifact = await created.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("source_grounded", artifact!.SourceBasis);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var bundle = await db.SourceEvidenceBundles.SingleAsync(b => b.Id == bundleId);
            bundle.IsDeleted = true;
            bundle.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var refreshed = await user.Client.PostAsJsonAsync($"/api/learning-artifacts/{artifact.Id}/refresh-status", new { reason = "source deleted" });
        refreshed.EnsureSuccessStatusCode();
        var stale = await refreshed.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("stale", stale!.ArtifactStatus);
        Assert.Contains("source_evidence_stale_or_missing", stale.Safety.Warnings);
    }

    [Fact]
    public async Task TutorAndWikiArtifacts_AreMirroredIntoUnifiedLifecycleSafely()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "artifacts-mirror");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Artifact Mirror");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, topicId, DateTime.UtcNow);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ILearningArtifactService>();
        var teaching = new TeachingArtifact
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            TopicId = topicId,
            SessionId = sessionId,
            TutorActionTraceId = Guid.NewGuid(),
            ArtifactType = "mermaid_graph",
            Title = "Concept graph",
            Content = "graph TD\nA --> B",
            RenderFormat = "mermaid",
            Status = "ready",
            Provider = "tutor-v2",
            MetadataJson = "{}",
            CreatedAt = DateTime.UtcNow
        };
        db.TeachingArtifacts.Add(teaching);
        await db.SaveChangesAsync();

        var mirrored = await service.MirrorTeachingArtifactAsync(
            user.UserId,
            teaching,
            new TutorTurnStateDto
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                SessionId = sessionId,
                ActiveConceptKey = "fractions",
                ActiveConceptLabel = "Fractions"
            });

        Assert.Equal("mermaid_diagram", mirrored.ArtifactType);
        Assert.Equal(teaching.Id, mirrored.TeachingArtifactId);
        Assert.Equal("tutor", mirrored.Origin);
        Assert.DoesNotContain(user.UserId.ToString(), JsonSerializer.Serialize(mirrored), StringComparison.OrdinalIgnoreCase);

        var wiki = await service.BuildArtifactForWikiSectionAsync(user.UserId, topicId, "core-notes");
        Assert.Equal("wiki_study_note", wiki.ArtifactType);
        Assert.Equal("wiki", wiki.Origin);
        Assert.True(wiki.SourceBasis is "wiki_backed" or "evidence_insufficient");
    }

    [Fact]
    public async Task Ownership_UserCannotReadOrRefreshOtherUsersArtifact()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "artifacts-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "artifacts-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Owned Artifact");

        var create = await owner.Client.PostAsJsonAsync("/api/learning-artifacts", new
        {
            topicId,
            artifactType = "concept_summary",
            artifactStatus = "ready",
            origin = "manual",
            renderFormat = "markdown",
            title = "Owned summary",
            safeContent = "Safe bounded summary.",
            sourceBasis = "model_assisted"
        });
        create.EnsureSuccessStatusCode();
        var artifact = await create.Content.ReadFromJsonAsync<LearningArtifactDto>();

        var readOther = await other.Client.GetAsync($"/api/learning-artifacts/{artifact!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, readOther.StatusCode);

        var refreshOther = await other.Client.PostAsJsonAsync($"/api/learning-artifacts/{artifact.Id}/refresh-status", new { reason = "nope" });
        Assert.Equal(HttpStatusCode.NotFound, refreshOther.StatusCode);
    }
}
