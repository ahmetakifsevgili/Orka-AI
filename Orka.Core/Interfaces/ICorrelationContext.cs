namespace Orka.Core.Interfaces;

/// <summary>
/// Request boyunca tüm katmanlara ve background task'lara taşınan
/// korelasyon kimliği. Her log satırına enjekte edilir.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; set; }
}
