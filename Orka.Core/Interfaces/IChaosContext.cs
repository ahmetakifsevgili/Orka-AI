namespace Orka.Core.Interfaces;

/// <summary>
/// Request başına hangi AI sağlayıcılarının "kaos" modunda çalışacağını taşır.
/// Controller/Middleware tarafından doldurulur; servisler tarafından okunur.
/// </summary>
public interface IChaosContext
{
    /// <summary>Verilen sağlayıcı adı için kaos aktif mi?</summary>
    bool IsProviderFailing(string providerName);

    /// <summary>Kaos hedefini ayarla (Controller tarafından çağrılır).</summary>
    void SetFailingProvider(string providerName);
}
