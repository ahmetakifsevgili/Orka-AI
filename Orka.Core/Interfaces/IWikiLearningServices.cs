using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IWikiLearningAssistant
{
    IAsyncEnumerable<WikiStreamEventDto> StreamAsync(
        WikiLearningRequestDto request,
        CancellationToken ct = default);
}

public interface IWikiEvidenceService
{
    Task<WikiEvidenceBundleDto> BuildAsync(
        WikiLearningRequestDto request,
        CancellationToken ct = default);

    Task<WikiWorkspaceStateDto> GetWorkspaceStateAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);
}

public interface IWikiAnswerPolicyEngine
{
    WikiAnswerPolicyDto BuildPolicy(WikiLearningRequestDto request, WikiEvidenceBundleDto evidence);
}

public interface IWikiCitationGuard
{
    WikiCitationGuardResultDto Apply(
        string answer,
        WikiEvidenceBundleDto evidence,
        WikiAnswerPolicyDto policy);
}

public interface IWikiArtifactService
{
    Task<IReadOnlyList<TeachingArtifactDto>> BuildArtifactsAsync(
        WikiLearningRequestDto request,
        WikiEvidenceBundleDto evidence,
        WikiCitationGuardResultDto guardedAnswer,
        CancellationToken ct = default);
}
