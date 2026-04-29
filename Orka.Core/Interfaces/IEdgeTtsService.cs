using System.Threading;
using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface IEdgeTtsService
{
    /// <summary>
    /// Edge-TTS kullanarak podcast script'ini (HOCA, ASISTAN rolleriyle) MP3'e çevirir.
    /// </summary>
    Task<byte[]> SynthesizeDialogueAsync(string script, CancellationToken ct = default);
}
