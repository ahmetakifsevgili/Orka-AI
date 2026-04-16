namespace Orka.Core.DTOs.Code;

/// <summary>
/// Kod çalıştırma isteği.
/// Stdin: interactive programlar için standart girdi (opsiyonel).
/// SessionId: sağlanırsa çalıştırma sonucu Redis'e yazılır; TutorAgent bir sonraki mesajda okur.
/// </summary>
public record CodeRunRequest(string Code, string Language = "csharp", string? Stdin = null, Guid? SessionId = null);
