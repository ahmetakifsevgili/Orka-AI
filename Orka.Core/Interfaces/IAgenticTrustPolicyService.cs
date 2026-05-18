using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IAgenticTrustPolicyService
{
    Task<AgenticTrustCheckResultDto> CheckUserMessageAsync(Guid userId, AgenticTrustCheckRequestDto request, CancellationToken ct = default);
    Task<AgenticTrustCheckResultDto> CheckSourceContentAsync(Guid userId, AgenticTrustCheckRequestDto request, CancellationToken ct = default);
    Task<AgenticTrustCheckResultDto> CheckToolRequestAsync(Guid userId, AgenticTrustCheckRequestDto request, CancellationToken ct = default);
    Task<AgenticTrustCheckResultDto> CheckTutorResponseAsync(Guid userId, AgenticTrustCheckRequestDto request, CancellationToken ct = default);
    Task<AgenticTrustCheckResultDto> CheckMemoryWriteAsync(Guid userId, AgenticTrustCheckRequestDto request, CancellationToken ct = default);
    Task<AgenticTrustCheckResultDto> CheckCitationSetAsync(Guid userId, AgenticTrustCheckRequestDto request, CancellationToken ct = default);
    Task<AgenticTrustCheckResultDto> CheckPublicPayloadAsync(Guid userId, AgenticTrustCheckRequestDto request, CancellationToken ct = default);
    Task<AgenticTrustRuntimeSummaryDto> GetTrustRuntimeSummaryAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, CancellationToken ct = default);
    Task<AgenticTrustRuntimeSummaryDto> EvaluateKnownFixturesAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, CancellationToken ct = default);
}
