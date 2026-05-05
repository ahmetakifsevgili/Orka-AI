# Backend Production Hardening Decision Matrix

Every relevant backend feature found in `D:\Orka` is assigned one explicit state.

| Feature | Dirty source files | Target files | Decision | Safety gate | Config flag | Tests/proof |
|---|---|---|---|---|---|---|
| SourcesQuery plugin | `SourcesQueryPlugin.cs` | `SourcesQueryPlugin.cs`, `/api/tools/capabilities` | INTEGRATED_AND_TESTED | auth/user isolation | n/a | contract/lifecycle source tests |
| ReviewQuery plugin | `ReviewQueryPlugin.cs` | `ReviewQueryPlugin.cs`, durable ReviewItem APIs | INTEGRATED_AND_TESTED | auth/user isolation | n/a | review contract tests |
| Flashcard plugin | `FlashcardPlugin.cs` | `FlashcardPlugin.cs`, flashcard APIs | INTEGRATED_AND_TESTED | auth/user isolation | n/a | flashcard contract tests |
| DailyChallenge plugin | `DailyChallengePlugin.cs` | `DailyChallengePlugin.cs`, daily challenge APIs | INTEGRATED_AND_TESTED | auth/user isolation | n/a | idempotency contract tests |
| Bookmark plugin/API | `BookmarkPlugin.cs`, bookmark model/controller | `BookmarkPlugin.cs`, `BookmarksController`, `Bookmark` | INTEGRATED_AND_TESTED | auth/user isolation | n/a | `test_bookmarks_crud_and_empty_state` |
| Semantic Kernel telemetry | `PluginTelemetryFilter.cs` | `PluginTelemetryFilter.cs`, `Program.cs` | INTEGRATED_AND_TESTED | non-blocking logs | n/a | build + capability tests |
| Tool capability model | dirty had no central contract | `ToolContracts.cs`, `ToolCapabilityService`, `ToolsController` | INTEGRATED_AND_TESTED | auth required | n/a | `ToolCapabilityContractTests`, contract test |
| Wolfram | `WolframAlphaPlugin.cs` | disabled `WolframAlphaPlugin.cs` stub | DISABLED_WITH_RUNTIME_STUB | provider key required before enable | `AI:WolframAlpha:AppId` | capability/stub tests |
| IDE/code execution SK tool | `IdeExecutionPlugin.cs` | disabled `IdeExecutionPlugin.cs` stub; existing sandbox API preserved | BETA_ADMIN_OR_DEV_ONLY | dev/admin + sandbox only | `Tools:IdeExecution:Enabled` | capability test; `/api/code/execute` guard |
| Weather | `WeatherGeographyPlugin.cs` | disabled `WeatherGeographyPlugin.cs` stub | DISABLED_WITH_RUNTIME_STUB | beta only | `Tools:Weather:Enabled` | capability test |
| News | `NewsPlugin.cs` | disabled `NewsPlugin.cs` stub | DISABLED_WITH_RUNTIME_STUB | provider key required | `AI:NewsAPI:ApiKey` | capability test |
| Crypto | `CryptoDataPlugin.cs` | disabled `CryptoDataPlugin.cs` stub | DISABLED_WITH_RUNTIME_STUB | beta only; no financial advice | `Tools:Crypto:Enabled` | capability test |
| Visual/Pollinations | `VisualGeneratorPlugin.cs` | `VisualGeneratorPlugin.cs` + capability status | INTEGRATED_BEHIND_GATE | beta visual CTA | `AI:VisualGeneration:Enabled` | capability test |
| Mermaid | prompt/metadata behavior | `ChatMetadataService`, capability status | INTEGRATED_AND_TESTED | text-only | n/a | metadata tests from prior addendum |
| YouTube pedagogy | `YouTubeTranscriptPlugin.cs` | `YouTubeTranscriptPlugin.cs`, capability status | INTEGRATED_BEHIND_GATE | pedagogy-only by default | `AI:YouTube:Enabled` | prior pedagogy docs/probes |
| Tavily web | `TavilySearchPlugin.cs` | hardened `TavilySearchPlugin.cs` | INTEGRATED_BEHIND_GATE | provider key | `AI:Tavily:ApiKey` | no-key disabled response; build/tests |
| Wikipedia | `WikipediaPlugin.cs` | existing plugin | INTEGRATED_AND_TESTED | public API, timeout | n/a | Korteks lifecycle |
| Academic search | `AcademicSearchPlugin.cs` | existing plugin | INTEGRATED_BEHIND_GATE | provider/public availability | n/a | capability/runtime classification |
| Push subscriptions | dirty push/Firebase model | `PushSubscription`, controllers | INTEGRATED_AND_TESTED | auth/user isolation | provider optional | push subscription contract test |
| SRS/Daily/push workers | worker code/features | background queue + services | NOT_PORTED_WITH_REASON | needs scheduling/load hardening | future flags | production hardening |
| Cost tracking ledger | dirty cost concepts | `ITokenCostEstimator` exists | NOT_PORTED_WITH_REASON | avoid partial ledger | n/a | production hardening |
| TestCleanup | dirty dev cleanup endpoint | not public | NOT_PORTED_WITH_REASON | destructive; never public | `Tools:DevCleanup:Enabled` only if reintroduced | explicit capability internal row |

## Notes

- `NOT_PORTED_WITH_REASON` does not mean forgotten. It means the feature is intentionally excluded from public/backend-core behavior until a safe production hardening design exists.
- High-risk tools are visible through the capability contract so frontend and runtime code do not infer availability from prose.
- Missing provider keys must produce disabled/degraded metadata, not backend startup crashes.
