using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IGeminiToolCallingService
{
    Task<GeminiToolChatResponse> GenerateToolChatAsync(
        GeminiToolChatRequest request,
        CancellationToken ct = default);
}

public interface IGeminiFunctionDeclarationCatalog
{
    IReadOnlyList<GeminiFunctionDeclaration> GetTutorSafeDeclarations();
    string? ResolveTutorToolId(string geminiFunctionName);
    string? ResolveGeminiFunctionName(string tutorToolId);
}

public interface IGeminiTutorToolAdvisoryService
{
    Task<GeminiTutorToolAdvisoryResult> ReviewTutorToolPlanAsync(
        GeminiTutorToolAdvisoryRequest request,
        CancellationToken ct = default);
}
