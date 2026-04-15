namespace Orka.Core.Interfaces;

/// <summary>
/// Piston v2 API üzerinden kod çalıştırma servisinin sözleşmesi.
/// Piston: https://piston.readthedocs.io/en/latest/api-v2/
/// </summary>
public interface IPistonService
{
    Task<PistonResult> ExecuteAsync(string code, string language = "csharp");
}

/// <summary>Piston'dan dönen çalıştırma sonucu.</summary>
public record PistonResult(string Stdout, string Stderr, bool Success);
