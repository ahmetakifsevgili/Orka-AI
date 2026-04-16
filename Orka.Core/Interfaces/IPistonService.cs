namespace Orka.Core.Interfaces;

/// <summary>
/// Piston v2 API üzerinden kod çalıştırma servisinin sözleşmesi.
/// Piston: https://piston.readthedocs.io/en/latest/api-v2/
/// </summary>
public interface IPistonService
{
    Task<PistonResult> ExecuteAsync(string code, string language = "csharp", string? stdin = null);

    /// <summary>Piston API'nin desteklediği çalışma zamanı listesini döner.</summary>
    Task<IReadOnlyList<PistonRuntime>> GetRuntimesAsync();
}

/// <summary>Piston'dan dönen çalıştırma sonucu.</summary>
public record PistonResult(string Stdout, string Stderr, bool Success);

/// <summary>Piston API'nin döndürdüğü dil/runtime bilgisi.</summary>
public record PistonRuntime(string Language, string Version, IReadOnlyList<string> Aliases);
