namespace Orka.Core.DTOs.Code;

public record CodeRunRequest(string Code, string Language = "csharp");
