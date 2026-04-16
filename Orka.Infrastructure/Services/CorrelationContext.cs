using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// AsyncLocal tabanlı Correlation ID taşıyıcısı.
/// Scoped olarak register edilir — her HTTP request kendi ID'sini taşır.
/// Task.Run ile başlatılan background task'larda ID dışarıda capture edilmelidir:
///   var capturedId = _correlationContext.CorrelationId;
///   Task.Run(() => { /* capturedId kullan */ });
/// </summary>
public class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; set; } = string.Empty;
}
