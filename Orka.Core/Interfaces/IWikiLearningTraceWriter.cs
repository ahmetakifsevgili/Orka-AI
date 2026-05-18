using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IWikiLearningTraceWriter
{
    Task<WikiBlockDto?> RecordTutorExplanationAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordStudentQuestionAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordQuizResultAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordMisconceptionAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordRepairNoteAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordWorkedExampleAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordSourceNoteAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordArtifactLinkAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
    Task<WikiBlockDto?> RecordManualNoteAsync(WikiLearningTraceRequestDto request, CancellationToken ct = default);
}
