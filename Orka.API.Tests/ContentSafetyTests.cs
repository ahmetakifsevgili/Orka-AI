using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orka.API.Services;
using Orka.Core.Entities;
using Orka.Core.Exceptions;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class ContentSafetyTests
{
    [Fact]
    public void CorsPolicy_RequiresExplicitOriginsOutsideDevelopment()
    {
        var empty = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        Assert.Throws<InvalidOperationException>(() => CorsStartupPolicyResolver.Resolve(empty, new TestEnvironment("Production")));

        var wildcard = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "*" })
            .Build();
        Assert.Throws<InvalidOperationException>(() => CorsStartupPolicyResolver.Resolve(wildcard, new TestEnvironment("Staging")));
    }

    [Fact]
    public void CorsPolicy_UsesLocalhostDefaultsInDevelopment()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var policy = CorsStartupPolicyResolver.Resolve(config, new TestEnvironment("Development"));

        Assert.False(policy.AllowAnyOrigin);
        Assert.Contains("http://localhost:3000", policy.AllowedOrigins);
        Assert.Contains("http://127.0.0.1:3000", policy.AllowedOrigins);
    }

    [Fact]
    public void CorsPolicy_AcceptsExplicitProductionOrigin()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://app.example.com"
            })
            .Build();

        var policy = CorsStartupPolicyResolver.Resolve(config, new TestEnvironment("Production"));

        Assert.False(policy.AllowAnyOrigin);
        Assert.Equal(["https://app.example.com"], policy.AllowedOrigins);
    }

    [Fact]
    public async Task SourceUpload_ValidMarkdown_Succeeds()
    {
        using var factory = new ApiSmokeFactory();
        var user = await RegisterAuthenticatedClientAsync(factory);
        var topicId = await CreateTopicAsync(factory, user.UserId);

        var response = await UploadAsync(user.Client, topicId, Encoding.UTF8.GetBytes("# Baslik\nKaynak metni"), "notes.md", "text/markdown");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SourceUpload_StoresFileSizeAndDeleteRecalculatesUserStorage()
    {
        using var factory = new ApiSmokeFactory();
        var user = await RegisterAuthenticatedClientAsync(factory);
        var topicId = await CreateTopicAsync(factory, user.UserId);
        var bytes = Encoding.UTF8.GetBytes("storage accounting source");

        var response = await UploadAsync(user.Client, topicId, bytes, "storage.txt", "text/plain");

        response.EnsureSuccessStatusCode();
        using var responseJson = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var sourceId = responseJson.RootElement.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var source = await db.LearningSources.SingleAsync(s => s.Id == sourceId);
            var dbUser = await db.Users.SingleAsync(u => u.Id == user.UserId);

            Assert.Equal(bytes.LongLength, source.FileSizeBytes);
            Assert.True(dbUser.StorageUsedMB > 0);
        }

        var delete = await user.Client.DeleteAsync($"/api/sources/{sourceId}");

        delete.EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var dbUser = await db.Users.SingleAsync(u => u.Id == user.UserId);

            Assert.Equal(0d, dbUser.StorageUsedMB, precision: 6);
        }
    }

    [Fact]
    public async Task SourceUpload_MultipartBodyLimit_ReturnsPayloadTooLargeBeforeControllerWork()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["ContentSafety:Uploads:MaxFileBytes"] = "10000",
                ["ContentSafety:Uploads:MaxMultipartBodyBytes"] = "128"
            });
        var user = await RegisterAuthenticatedClientAsync(factory);
        var topicId = await CreateTopicAsync(factory, user.UserId);

        var response = await UploadAsync(user.Client, topicId, Encoding.UTF8.GetBytes(new string('a', 512)), "body.txt", "text/plain");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.False(await SourceExistsAsync(factory, user.UserId, topicId));
    }

    [Fact]
    public async Task SourceUpload_OversizeFile_ReturnsPayloadTooLargeAndDoesNotCreateSource()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["ContentSafety:Uploads:MaxFileBytes"] = "16"
            });
        var user = await RegisterAuthenticatedClientAsync(factory);
        var topicId = await CreateTopicAsync(factory, user.UserId);

        var response = await UploadAsync(user.Client, topicId, Encoding.UTF8.GetBytes(new string('a', 32)), "notes.txt", "text/plain");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.False(await SourceExistsAsync(factory, user.UserId, topicId));
    }

    [Fact]
    public async Task SourceUpload_BinaryTextOrMismatchedPdf_ReturnsBadRequest()
    {
        using var factory = new ApiSmokeFactory();
        var user = await RegisterAuthenticatedClientAsync(factory);
        var topicId = await CreateTopicAsync(factory, user.UserId);

        var binaryText = await UploadAsync(user.Client, topicId, [0, 1, 2, 3, 0, 255], "notes.txt", "text/plain");
        var fakePdf = await UploadAsync(user.Client, topicId, Encoding.UTF8.GetBytes("not a pdf"), "notes.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.BadRequest, binaryText.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fakePdf.StatusCode);
    }

    [Fact]
    public async Task SourceUpload_ChunkAndEmbeddingQuotaRejectBeforePersistingSource()
    {
        using var chunkFactory = new ApiSmokeFactory(
            "Development",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["ContentSafety:Uploads:MaxChunksPerSource"] = "1"
            });
        var chunkUser = await RegisterAuthenticatedClientAsync(chunkFactory);
        var chunkTopicId = await CreateTopicAsync(chunkFactory, chunkUser.UserId);
        var longText = Encoding.UTF8.GetBytes(string.Join(' ', Enumerable.Repeat("uzun kaynak metni", 400)));

        var chunkResponse = await UploadAsync(chunkUser.Client, chunkTopicId, longText, "long.txt", "text/plain");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunkResponse.StatusCode);
        Assert.False(await SourceExistsAsync(chunkFactory, chunkUser.UserId, chunkTopicId));

        using var embeddingFactory = new ApiSmokeFactory(
            "Development",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["ContentSafety:Uploads:MaxEmbeddingChunksPerUserPerDay"] = "0"
            });
        var embeddingUser = await RegisterAuthenticatedClientAsync(embeddingFactory);
        var embeddingTopicId = await CreateTopicAsync(embeddingFactory, embeddingUser.UserId);

        var embeddingResponse = await UploadAsync(embeddingUser.Client, embeddingTopicId, Encoding.UTF8.GetBytes("kisa kaynak"), "quota.txt", "text/plain");

        Assert.Equal((HttpStatusCode)429, embeddingResponse.StatusCode);
        Assert.False(await SourceExistsAsync(embeddingFactory, embeddingUser.UserId, embeddingTopicId));
    }

    [Fact]
    public async Task SourceUpload_UserBackpressure_ReturnsTooManyRequests()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["ContentSafety:Uploads:MaxUploadsPerUserPerHour"] = "0"
            });
        var user = await RegisterAuthenticatedClientAsync(factory);
        var topicId = await CreateTopicAsync(factory, user.UserId);

        var response = await UploadAsync(user.Client, topicId, Encoding.UTF8.GetBytes("kisa kaynak"), "rate.txt", "text/plain");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    [Fact]
    public async Task KorteksResearchFile_UserBackpressure_ReturnsTooManyRequestsBeforeProviderWork()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["ContentSafety:Uploads:MaxKorteksFileResearchPerUserPerHour"] = "0"
            });
        var user = await RegisterAuthenticatedClientAsync(factory);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Backpressure test"), "Topic");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("kisa arastirma dosyasi"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "korteks.txt");

        var response = await user.Client.PostAsync("/api/korteks/research-file", form);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    [Fact]
    public void UploadGuard_RejectsPdfPageAndExtractedCharacterLimit()
    {
        var guard = new UploadContentSafetyGuard(Options.Create(new ContentSafetyOptions
        {
            Uploads = new UploadContentSafetyOptions
            {
                MaxPdfPages = 2,
                MaxExtractedChars = 8
            }
        }));

        Assert.Throws<ContentSafetyException>(() => guard.ValidateExtractedDocument(
        [
            new ExtractedPage(1, "abc"),
            new ExtractedPage(2, "def"),
            new ExtractedPage(3, "ghi")
        ]));
        Assert.Throws<ContentSafetyException>(() => guard.ValidateExtractedDocument([new ExtractedPage(1, "123456789")]));
    }

    [Fact]
    public void FileExtraction_RejectsPdfByActualPageCountBeforeTextPageCount()
    {
        var extractor = CreateExtractor(new UploadContentSafetyOptions
        {
            MaxPdfPages = 1,
            MaxExtractedChars = 10_000
        });

        var pdf = BuildPdf("first page", "second page");

        var ex = Assert.Throws<ContentSafetyException>(() => extractor.ExtractWithPages("two-pages.pdf", pdf));
        Assert.Equal(413, ex.StatusCode);
        Assert.Contains("sayfa", ex.PublicMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileExtraction_RejectsPdfWhenRunningCharacterLimitIsExceeded()
    {
        var extractor = CreateExtractor(new UploadContentSafetyOptions
        {
            MaxPdfPages = 10,
            MaxExtractedChars = 8
        });

        var pdf = BuildPdf("12345", "67890");

        var ex = Assert.Throws<ContentSafetyException>(() => extractor.ExtractWithPages("long.pdf", pdf));
        Assert.Equal(413, ex.StatusCode);
        Assert.Contains("metni", ex.PublicMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorsPreflight_UsesConfiguredOriginAllowlist()
    {
        using var refreshSecret = UseProductionAuthSecrets();
        using var factory = new ApiSmokeFactory(
            "Production",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://app.example.com"
            });
        var client = factory.CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        allowed.Headers.TryAddWithoutValidation("Origin", "https://app.example.com");
        allowed.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        allowed.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type,authorization");

        var allowedResponse = await client.SendAsync(allowed);

        Assert.Equal("https://app.example.com", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var denied = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        denied.Headers.TryAddWithoutValidation("Origin", "https://evil.example.com");
        denied.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");

        var deniedResponse = await client.SendAsync(denied);

        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SecurityHeaders_AddsCspOutsideDevelopmentOnlyByDefault()
    {
        using var refreshSecret = UseProductionAuthSecrets();
        using var productionFactory = new ApiSmokeFactory(
            "Production",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://app.example.com"
            });
        var productionResponse = await productionFactory.CreateClient().GetAsync("/health/live");

        Assert.True(productionResponse.Headers.TryGetValues("Content-Security-Policy", out var cspValues));
        var csp = cspValues.Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("JWT", csp, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", csp, StringComparison.OrdinalIgnoreCase);

        using var developmentFactory = new ApiSmokeFactory();
        var developmentResponse = await developmentFactory.CreateClient().GetAsync("/health/live");

        Assert.False(developmentResponse.Headers.Contains("Content-Security-Policy"));
        Assert.False(developmentResponse.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    private static IDisposable UseProductionAuthSecrets() =>
        new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["JWT__Secret"] = "ORKA_TEST_JWT_SIGNING_SECRET_FOR_PRODUCTION_FACTORY_64_CHARS_2026",
            ["JWT__RefreshTokenHashSecret"] = "ORKA_TEST_REFRESH_HASH_SECRET_FOR_PRODUCTION_FACTORY_64_CHARS_2026",
            ["Cors__AllowedOrigins__0"] = "https://app.example.com"
        });

    private static async Task<TestUser> RegisterAuthenticatedClientAsync(ApiSmokeFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"content-{Guid.NewGuid():N}@orka.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Content",
            lastName = "Safety",
            email,
            password = "ContentPass123!"
        });
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = body.RootElement.GetProperty("token").GetString()
                    ?? throw new InvalidOperationException("Register token missing.");
        var userId = Guid.Parse(body.RootElement.GetProperty("user").GetProperty("id").GetString()!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestUser(client, userId);
    }

    private static async Task<Guid> CreateTopicAsync(ApiSmokeFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Content Safety Topic",
            Category = "Genel",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.Id;
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid topicId,
        byte[] bytes,
        string fileName,
        string contentType)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(topicId.ToString()), "TopicId");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        return await client.PostAsync("/api/sources/upload", form);
    }

    private static async Task<bool> SourceExistsAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.LearningSources.AnyAsync(s => s.UserId == userId && s.TopicId == topicId);
    }

    private sealed record TestUser(HttpClient Client, Guid UserId);

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (key, value) in values)
            {
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previousValues)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static FileExtractionService CreateExtractor(UploadContentSafetyOptions uploadOptions) =>
        new(
            NullLogger<FileExtractionService>.Instance,
            new UploadContentSafetyGuard(Options.Create(new ContentSafetyOptions { Uploads = uploadOptions })));

    private static byte[] BuildPdf(params string[] pageTexts)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>"
        };

        var pageObjectNumbers = new List<int>();
        for (var i = 0; i < pageTexts.Length; i++)
        {
            pageObjectNumbers.Add(3 + i);
        }

        objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(n => $"{n} 0 R"))}] /Count {pageTexts.Length} >>");

        var contentStart = 3 + pageTexts.Length;
        for (var i = 0; i < pageTexts.Length; i++)
        {
            var contentObjectNumber = contentStart + i;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {contentStart + pageTexts.Length} 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
        }

        foreach (var text in pageTexts)
        {
            var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            var stream = $"BT /F1 12 Tf 72 720 Td ({escaped}) Tj ET";
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        using var ms = new MemoryStream();
        void Write(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            ms.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n");
        Write("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            Write($"{offset:0000000000} 00000 n \n");

        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Orka.API.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
