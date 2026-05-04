using System;

namespace Orka.Core.DTOs.Korteks;

public sealed record ToolCallEvidenceDto(
    string ToolName,
    string Provider,
    bool Invoked,
    bool Success,
    string? FailureReason,
    int ResultCount,
    long? DurationMs,
    string? DegradedMarker,
    DateTimeOffset Timestamp);
