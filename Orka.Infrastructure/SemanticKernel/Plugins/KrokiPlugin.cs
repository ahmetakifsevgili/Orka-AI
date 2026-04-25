using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// KrokiPlugin — Metin tabanlı diyagram oluşturucu.
///
/// Kroki API (ücretsiz, key gerekmez):
///   - PlantUML, Mermaid, GraphViz, Excalidraw, BPMN vb. diyagramları destekler
///   - Base64 + zlib (veya raw text) alıp SVG döner
///   - Raporlara mimari şemalar ve karmaşık diyagramlar eklemek için kullanılır
///
/// URL: https://kroki.io/
/// </summary>
public class KrokiPlugin
{
    [KernelFunction, Description(
        "Metin formatında (Mermaid, PlantUML, GraphViz, Structurizr vb.) yazılmış bir diyagram kodunu SVG/PNG görseline dönüştürür. " +
        "Raporlara mimari çizimler, akış şemaları veya zihin haritaları eklemek için kullan. " +
        "Bu metod metinden görsel URL'si üretip Markdown Image tag olarak döner.")]
    public Task<string> GenerateDiagram(
        [Description("Diyagram türü (mermaid, plantuml, graphviz, excalidraw vb.)")] string diagramType,
        [Description("Diyagramın raw kodu (text). Doğru sözdizimine (syntax) sahip olduğundan emin ol.")] string diagramSource)
    {
        try
        {
            // Kroki requires payload to be: base64(zlib(source)) url-safe
            var bytes = Encoding.UTF8.GetBytes(diagramSource);
            using var output = new MemoryStream();
            using (var zipStream = new DeflateStream(output, CompressionMode.Compress, true))
            {
                zipStream.Write(bytes, 0, bytes.Length);
            }
            output.Position = 0;
            var compressedBytes = output.ToArray();
            
            // Base64 URL-safe encoding (RFC 4648)
            var base64 = Convert.ToBase64String(compressedBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

            var format = diagramType.ToLowerInvariant() == "excalidraw" ? "svg" : "svg";
            var url = $"https://kroki.io/{diagramType.ToLowerInvariant()}/{format}/{base64}";

            return Task.FromResult($"![Kroki Diagram]({url})\n*(Görsel yüklenmezse diye kaynak kod:)*\n```{diagramType}\n{diagramSource}\n```\n");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[Diyagram oluşturulamadı: {ex.Message}]");
        }
    }
}
