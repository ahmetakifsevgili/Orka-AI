using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orka.Infrastructure.SemanticKernel.Filters;
using Orka.Infrastructure.SemanticKernel.Plugins;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using System;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Orka.API.Extensions
{
    public static class AiProviderExtensions
    {
        public static IServiceCollection AddAiProviders(this IServiceCollection services, IConfiguration configuration)
        {
            // LLMOps: Token/Cost Estimator
            services.AddSingleton<ITokenCostEstimator, TokenCostEstimator>();

            // Semantic Kernel Plugins
            services.AddScoped<WikiPlugin>();
            services.AddScoped<TopicPlugin>();
            if (configuration.GetValue("AI:Tavily:Enabled", true))
            {
                services.AddScoped<TavilySearchPlugin>();
            }
            services.AddScoped<WikipediaPlugin>();
            services.AddScoped<AcademicSearchPlugin>();
            services.AddScoped<YouTubeTranscriptPlugin>();
            services.AddScoped<SourcesQueryPlugin>();
            services.AddScoped<ReviewQueryPlugin>();
            services.AddScoped<FlashcardPlugin>();
            services.AddScoped<DailyChallengePlugin>();
            services.AddScoped<BookmarkPlugin>();
            services.AddScoped<LearningModePlugin>();
            services.AddScoped<AgentDecisionPlugin>();
            services.AddScoped<VisualGeneratorPlugin>();
            services.AddScoped<WolframAlphaPlugin>();
            services.AddScoped<IdeExecutionPlugin>();
            services.AddScoped<WeatherGeographyPlugin>();
            services.AddScoped<NewsPlugin>();
            services.AddScoped<CryptoDataPlugin>();
            services.AddScoped<PluginTelemetryFilter>();

            // Korteks dosya çıkarma servisi
            services.AddScoped<FileExtractionService>();

            // ── Named HttpClients + Microsoft.Extensions.Http.Resilience ──────────────────
            services.AddHttpClient("GitHubModels", c => c.Timeout = TimeSpan.FromSeconds(120))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    UseProxy = false,
                    SslOptions =
                    {
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ClientCertificates = new X509CertificateCollection()
                    }
                })
                .AddStandardResilienceHandler(o =>
                {
                    o.Retry.MaxRetryAttempts          = 1;
                    o.Retry.Delay                     = TimeSpan.FromMilliseconds(300);
                    o.AttemptTimeout.Timeout          = TimeSpan.FromSeconds(90);
                    o.TotalRequestTimeout.Timeout     = TimeSpan.FromSeconds(120);
                    o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(3);
                    o.CircuitBreaker.MinimumThroughput = 2;
                });

            services.AddHttpClient("CohereEmbed", c => c.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("Cohere", c => c.Timeout = TimeSpan.FromSeconds(30))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("Groq", c => c.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                })
                .AddStandardResilienceHandler(o =>
                {
                    o.Retry.MaxRetryAttempts          = 3;
                    o.Retry.Delay                     = TimeSpan.FromMilliseconds(500);
                    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                });

            services.AddHttpClient("Mistral", c => c.Timeout = TimeSpan.FromSeconds(60))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                })
                .AddStandardResilienceHandler(o =>
                {
                    o.Retry.MaxRetryAttempts          = 1;
                    o.Retry.Delay                     = TimeSpan.FromMilliseconds(300);
                    o.AttemptTimeout.Timeout          = TimeSpan.FromSeconds(45);
                    o.TotalRequestTimeout.Timeout     = TimeSpan.FromSeconds(60);
                    o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                });

            services.AddHttpClient("OpenRouter", c => c.Timeout = TimeSpan.FromSeconds(20))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("Cerebras", c => c.Timeout = TimeSpan.FromSeconds(10))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("SambaNova", c => c.Timeout = TimeSpan.FromSeconds(20))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("Gemini", c => c.Timeout = TimeSpan.FromSeconds(150))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    UseProxy = false,
                    SslOptions =
                    {
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ClientCertificates = new X509CertificateCollection()
                    }
                })
                .AddStandardResilienceHandler(o =>
                {
                    o.Retry.MaxRetryAttempts          = 1;
                    o.Retry.Delay                     = TimeSpan.FromMilliseconds(200);
                    o.AttemptTimeout.Timeout          = TimeSpan.FromSeconds(120);
                    o.TotalRequestTimeout.Timeout     = TimeSpan.FromSeconds(150);
                    o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                });

            services.AddHttpClient("Tavily", c => c.Timeout = TimeSpan.FromSeconds(20))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                })
                .AddStandardResilienceHandler(o =>
                {
                    o.Retry.MaxRetryAttempts          = 2;
                    o.Retry.Delay                     = TimeSpan.FromMilliseconds(500);
                    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                });

            services.AddHttpClient("Wikipedia", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(15);
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform; aakif1345@gmail.com)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("YouTube", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(15);
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("YouTubeTranscript", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(12);
                c.BaseAddress = new Uri(configuration["AI:YouTube:TranscriptBaseUrl"] ?? "https://www.youtube.com/");
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("WolframAlpha", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
                c.BaseAddress = new Uri(configuration["AI:WolframAlpha:BaseUrl"] ?? "https://api.wolframalpha.com/");
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("News", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(12);
                c.BaseAddress = new Uri(configuration["AI:NewsAPI:BaseUrl"] ?? "https://newsapi.org/");
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("Weather", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
                c.BaseAddress = new Uri(configuration["Tools:Weather:BaseUrl"] ?? "https://api.openweathermap.org/");
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("Geocoding", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(8);
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("MarketData", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
                c.BaseAddress = new Uri(configuration["Tools:Crypto:BaseUrl"] ?? "https://api.coingecko.com/");
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("RealWorldEvidence", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(12);
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform; real-world teaching evidence)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });

            services.AddHttpClient("Piston", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
                c.DefaultRequestHeaders.Add("User-Agent", "OrkaAI/1.0 (educational platform)");
            })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });
            services.AddScoped<IPistonService, PistonService>();

            // ── GitHub Models + Agent Factory + Embeddings ────────────────────────────
            services.AddScoped<IGitHubModelsService, GitHubModelsService>();
            services.AddScoped<IEmbeddingService, CohereEmbeddingService>();
            services.AddScoped<IAIAgentFactory, AIAgentFactory>();

            services.AddScoped<IGroqService, GroqService>();
            services.AddScoped<IGeminiService, GeminiService>();
            services.AddScoped<IGeminiToolCallingService, GeminiToolCallingService>();
            services.AddScoped<IGeminiFunctionDeclarationCatalog, GeminiFunctionDeclarationCatalog>();
            services.AddScoped<IGeminiTutorToolAdvisoryService, GeminiTutorToolAdvisoryService>();
            services.AddScoped<IMistralService, MistralService>();
            services.AddScoped<IOpenRouterService, OpenRouterService>();
            services.AddScoped<ICerebrasService, CerebrasService>();
            services.AddScoped<ISambaNovaService, SambaNovaService>();
            services.AddScoped<ICohereService, CohereService>();

            // ── SEMANTIC KERNEL SETUP ──────────────────────────────────────────────────
            services.AddScoped<Kernel>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();

                var model   = config["AI:GitHubModels:Agents:Korteks:Model"] ?? "Meta-Llama-3.1-405B-Instruct";
                var apiKey  = config["AI:GitHubModels:Token"]                ?? throw new InvalidOperationException("AI:GitHubModels:Token eksik.");
                var baseUrl = config["AI:GitHubModels:BaseUrl"]              ?? "https://models.inference.ai.azure.com";

                var kernelBuilder = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(
                        modelId:  model,
                        apiKey:   apiKey,
                        endpoint: new Uri(baseUrl));

                var kernel = kernelBuilder.Build();

                kernel.Plugins.AddFromObject(sp.GetRequiredService<WikiPlugin>());
                kernel.Plugins.AddFromObject(sp.GetRequiredService<TopicPlugin>());
                if (config.GetValue("AI:Tavily:Enabled", true) &&
                    sp.GetService<TavilySearchPlugin>() is { } tavilyPlugin)
                {
                    kernel.Plugins.AddFromObject(tavilyPlugin);
                }
                kernel.Plugins.AddFromObject(sp.GetRequiredService<WikipediaPlugin>());
                kernel.Plugins.AddFromObject(sp.GetRequiredService<AcademicSearchPlugin>());
                kernel.Plugins.AddFromObject(sp.GetRequiredService<YouTubeTranscriptPlugin>());
                kernel.Plugins.AddFromObject(sp.GetRequiredService<SourcesQueryPlugin>(), "Sources");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<ReviewQueryPlugin>(), "Review");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<FlashcardPlugin>(), "Flashcards");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<DailyChallengePlugin>(), "DailyChallenge");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<BookmarkPlugin>(), "Bookmarks");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<LearningModePlugin>(), "LearningMode");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<AgentDecisionPlugin>(), "AgentDecision");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<VisualGeneratorPlugin>(), "Visuals");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<WolframAlphaPlugin>(), "Wolfram");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<IdeExecutionPlugin>(), "IdeExecution");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<WeatherGeographyPlugin>(), "Weather");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<NewsPlugin>(), "News");
                kernel.Plugins.AddFromObject(sp.GetRequiredService<CryptoDataPlugin>(), "Crypto");
                kernel.FunctionInvocationFilters.Add(sp.GetRequiredService<PluginTelemetryFilter>());
                        
                return kernel;
            });

            return services;
        }
    }
}
