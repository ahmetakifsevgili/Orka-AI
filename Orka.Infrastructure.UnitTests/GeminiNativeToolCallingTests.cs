using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class GeminiNativeToolCallingTests
{
    [Fact]
    public void Catalog_ExposesOnlyTutorSafeDeclarations()
    {
        var catalog = new GeminiFunctionDeclarationCatalog();

        var declarations = catalog.GetTutorSafeDeclarations();

        Assert.Contains(declarations, d => d.Name == "orka_wiki_search");
        Assert.Contains(declarations, d => d.Name == "orka_source_search");
        Assert.DoesNotContain(declarations, d => d.Name.Contains("ide_execution", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(declarations, d => d.Name.Contains("tavily", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("wiki_search", catalog.ResolveTutorToolId("orka_wiki_search"));
        Assert.Equal("orka_flashcard_query", catalog.ResolveGeminiFunctionName("flashcard_query"));
    }

    [Fact]
    public async Task GenerateToolChatAsync_ParsesFunctionCallWithoutTreatingItAsText()
    {
        var responseJson = """
            {
              "candidates": [
                {
                  "content": {
                    "role": "model",
                    "parts": [
                      {
                        "functionCall": {
                          "id": "call-1",
                          "name": "orka_wiki_search",
                          "args": {
                            "purpose": "Check wiki memory.",
                            "query": "integrals",
                            "required": true,
                            "riskLevel": "low"
                          }
                        },
                        "thoughtSignature": "secret-signature"
                      }
                    ]
                  },
                  "finishReason": "STOP"
                }
              ],
              "usageMetadata": {
                "promptTokenCount": 11,
                "candidatesTokenCount": 5,
                "thoughtsTokenCount": 7,
                "totalTokenCount": 23
              },
              "modelVersion": "gemini-3.5-flash"
            }
            """;
        var service = CreateService(responseJson);

        var result = await service.GenerateToolChatAsync(new GeminiToolChatRequest
        {
            Model = "gemini-3.5-flash",
            Contents =
            [
                new GeminiContent { Role = "user", Parts = [new GeminiPart { Text = "Need tool?" }] }
            ],
            FunctionDeclarations = new GeminiFunctionDeclarationCatalog().GetTutorSafeDeclarations()
        });

        Assert.Null(result.Text);
        Assert.Single(result.FunctionCalls);
        Assert.Equal("orka_wiki_search", result.FunctionCalls[0].Name);
        Assert.Equal("call-1", result.FunctionCalls[0].Id);
        Assert.Equal("secret-signature", result.FunctionCalls[0].ThoughtSignature);
        Assert.Equal(7, result.ThoughtsTokenCount);
    }

    [Fact]
    public async Task GenerateToolChatAsync_PreservesThoughtSignatureInFollowupFunctionResponseRequest()
    {
        var service = CreateService("{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"done\"}]},\"finishReason\":\"STOP\"}],\"modelVersion\":\"gemini-3.5-flash\"}");
        var modelContent = new GeminiContent
        {
            Role = "model",
            Parts =
            [
                new GeminiPart
                {
                    ThoughtSignature = "sig-123",
                    FunctionCall = new GeminiFunctionCall
                    {
                        Id = "call-1",
                        Name = "orka_wiki_search",
                        Args = JsonSerializer.SerializeToElement(new { query = "limits" })
                    }
                }
            ]
        };

        await service.GenerateToolChatAsync(new GeminiToolChatRequest
        {
            Model = "gemini-3.5-flash",
            Contents =
            [
                new GeminiContent { Role = "user", Parts = [new GeminiPart { Text = "Need tool?" }] },
                modelContent,
                new GeminiContent
                {
                    Role = "user",
                    Parts =
                    [
                        new GeminiPart
                        {
                            FunctionResponse = new GeminiFunctionResponse
                            {
                                Id = "call-1",
                                Name = "orka_wiki_search",
                                Response = JsonSerializer.SerializeToElement(new { output = "wiki ready" })
                            }
                        }
                    ]
                }
            ],
            FunctionDeclarations = new GeminiFunctionDeclarationCatalog().GetTutorSafeDeclarations()
        });

        Assert.Contains("\"thoughtSignature\":\"sig-123\"", service.Factory.LastRequestBody);
        Assert.Contains("\"functionResponse\"", service.Factory.LastRequestBody);
        Assert.Contains("\"id\":\"call-1\"", service.Factory.LastRequestBody);
    }

    [Fact]
    public async Task GenerateToolChatAsync_WhenGeminiDisabled_DoesNotSendRequest()
    {
        var factory = new CapturingHttpClientFactory("{}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Gemini:Enabled"] = "false",
            ["AI:Gemini:ApiKey"] = "test-key",
            ["AI:Gemini:UseVertexAi"] = "false",
            ["AI:Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta/models"
        }).Build();
        var service = new TestableGeminiToolCallingService(factory, config);

        var exception = await Assert.ThrowsAsync<ProviderConfigurationException>(() =>
            service.GenerateToolChatAsync(new GeminiToolChatRequest
            {
                Model = "gemini-3.5-flash",
                Contents =
                [
                    new GeminiContent { Role = "user", Parts = [new GeminiPart { Text = "Need tool?" }] }
                ],
                FunctionDeclarations = new GeminiFunctionDeclarationCatalog().GetTutorSafeDeclarations()
            }));

        Assert.Equal("Gemini", exception.Provider);
        Assert.Equal("AI:Gemini:Enabled", exception.KeyPath);
        Assert.Equal(string.Empty, factory.LastRequestBody);
    }

    [Fact]
    public async Task Advisory_AcceptsAllowedSuggestionAndRejectsDeniedSuggestion()
    {
        var catalog = new GeminiFunctionDeclarationCatalog();
        var gemini = new FakeGeminiToolCallingService(
            new GeminiFunctionCall
            {
                Name = "orka_wiki_search",
                Args = JsonSerializer.SerializeToElement(new { purpose = "Use wiki.", query = "integrals", required = false, riskLevel = "low" })
            },
            new GeminiFunctionCall
            {
                Name = "orka_news_search",
                Args = JsonSerializer.SerializeToElement(new { purpose = "Search news.", query = "integrals", required = false, riskLevel = "medium" })
            });
        var advisory = new GeminiTutorToolAdvisoryService(
            gemini,
            catalog,
            new FakeRuntime(deniedTool: "news"),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Gemini:ToolAdvisory:Enabled"] = "true",
                ["AI:Gemini:ToolAdvisory:Model"] = "gemini-3.5-flash"
            }).Build(),
            NullLogger<GeminiTutorToolAdvisoryService>.Instance);

        var result = await advisory.ReviewTutorToolPlanAsync(new GeminiTutorToolAdvisoryRequest
        {
            UserId = Guid.NewGuid(),
            TutorTurnStateId = Guid.NewGuid(),
            UserMessage = "Explain with wiki context.",
            CurrentToolPlans = []
        });

        Assert.Single(result.AcceptedSuggestions);
        Assert.Equal("wiki_search", result.AcceptedSuggestions[0].ToolId);
        Assert.Single(result.RejectedSuggestions);
        Assert.Equal("news", result.RejectedSuggestions[0].ToolId);
        Assert.Equal("denied_for_test", result.RejectedSuggestions[0].ReasonCode);
    }

    private static TestableGeminiToolCallingService CreateService(string responseJson)
    {
        var factory = new CapturingHttpClientFactory(responseJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Gemini:ApiKey"] = "test-key",
            ["AI:Gemini:UseVertexAi"] = "false",
            ["AI:Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta/models"
        }).Build();
        return new TestableGeminiToolCallingService(factory, config);
    }

    private sealed class TestableGeminiToolCallingService : GeminiToolCallingService
    {
        public CapturingHttpClientFactory Factory { get; }

        public TestableGeminiToolCallingService(CapturingHttpClientFactory factory, IConfiguration configuration)
            : base(factory, configuration, NullLogger<GeminiToolCallingService>.Instance)
        {
            Factory = factory;
        }
    }

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private readonly string _responseJson;
        public string LastRequestBody { get; private set; } = string.Empty;

        public CapturingHttpClientFactory(string responseJson)
        {
            _responseJson = responseJson;
        }

        public HttpClient CreateClient(string name) =>
            new(new Handler(this, _responseJson)) { BaseAddress = new Uri("https://example.test") };

        private sealed class Handler : HttpMessageHandler
        {
            private readonly CapturingHttpClientFactory _factory;
            private readonly string _responseJson;

            public Handler(CapturingHttpClientFactory factory, string responseJson)
            {
                _factory = factory;
                _responseJson = responseJson;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _factory.LastRequestBody = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
                };
            }
        }
    }

    private sealed class FakeGeminiToolCallingService : IGeminiToolCallingService
    {
        private readonly IReadOnlyList<GeminiFunctionCall> _calls;

        public FakeGeminiToolCallingService(params GeminiFunctionCall[] calls)
        {
            _calls = calls;
        }

        public Task<GeminiToolChatResponse> GenerateToolChatAsync(GeminiToolChatRequest request, CancellationToken ct = default) =>
            Task.FromResult(new GeminiToolChatResponse
            {
                Model = request.Model,
                FunctionCalls = _calls
            });
    }

    private sealed class FakeRuntime : IUnifiedToolRuntimeService
    {
        private readonly string _deniedTool;

        public FakeRuntime(string deniedTool)
        {
            _deniedTool = deniedTool;
        }

        public Task<ToolRuntimeDecisionDto> DecideAsync(Guid userId, ToolRuntimeRequestDto request, CancellationToken ct = default)
        {
            var denied = string.Equals(request.ToolId, _deniedTool, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new ToolRuntimeDecisionDto
            {
                ToolId = request.ToolId,
                Allowed = !denied,
                Decision = denied ? "deny" : "allow",
                ReasonCode = denied ? "denied_for_test" : "allowed_for_test",
                UserSafeReason = denied ? "Denied for test." : "Allowed for test.",
                CanGroundClaims = !denied
            });
        }

        public Task<ToolRuntimeResultDto> RecordResultAsync(Guid userId, ToolRuntimeResultDto result, CancellationToken ct = default) =>
            Task.FromResult(result);

        public Task<ToolRuntimeTraceDto?> GetToolRuntimeTraceAsync(Guid userId, Guid traceId, CancellationToken ct = default) =>
            Task.FromResult<ToolRuntimeTraceDto?>(null);

        public Task<IReadOnlyList<ToolRuntimeTraceDto>> GetRecentToolRuntimeTracesAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, int take = 20, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ToolRuntimeTraceDto>>(Array.Empty<ToolRuntimeTraceDto>());

        public Task<ToolGovernanceSummaryDto> GetToolGovernanceSummaryAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, CancellationToken ct = default) =>
            Task.FromResult(new ToolGovernanceSummaryDto());
    }
}
