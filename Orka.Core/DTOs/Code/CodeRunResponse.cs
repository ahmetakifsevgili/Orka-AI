namespace Orka.Core.DTOs.Code;

public record CodeRunResponse(
    string Stdout,
    string Stderr,
    bool Success,
    string Phase = "run",
    string? CompileError = null,
    string? RuntimeError = null,
    int? ExitCode = null,
    long DurationMs = 0,
    bool Truncated = false,
    string? SafeTutorSummary = null,
    string? Runtime = null);
