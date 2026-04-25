using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// QuickChartPlugin — Chart.js tabanlı grafik oluşturucu.
///
/// QuickChart API (ücretsiz, key gerekmez):
///   - JSON config tabanlı PNG grafik üretir
///   - Bar, line, pie, radar chart vb.
///   - Korteks raporlarında veri görselleştirme için kullanılır
///
/// URL: https://quickchart.io/
/// </summary>
public class QuickChartPlugin
{
    [KernelFunction, Description(
        "Verileri kullanarak PNG formatında bir grafik yazar. Bar, line, pie, radar veya doughnut grafik türlerini destekler. " +
        "Sana veri sağlandığında (örn. yıllara göre atıflar, performans skorları) bunu Markdown'da gösterebileceğin bir resim URL'si döndürür. " +
        "Raporlara veri destekli görseller eklemek için kullan.")]
    public Task<string> GenerateChart(
        [Description("Grafik türü: 'bar', 'line', 'pie', 'radar' veya 'doughnut'")] string chartType,
        [Description("Grafik başlığı")] string title,
        [Description("Virgülle ayrılmış etiketler sırası (örn. '2020,2021,2022')")] string labelsStr,
        [Description("Virgülle ayrılmış veri değerleri (örn. '10,20,30')")] string dataStr,
        [Description("Veri setinin etiketi (örn. 'Atıf Sayısı')")] string datasetLabel = "Değerler")
    {
        var labelsArray = string.Join(",", labelsStr.Split(',').Select(x => $"\"{x.Trim()}\""));
        var dataArray = string.Join(",", dataStr.Split(',').Select(x => x.Trim()));

        // Chart.js config format
        var chartConfig = $@"{{
            type: '{chartType}',
            data: {{
                labels: [{labelsArray}],
                datasets: [{{
                    label: '{datasetLabel}',
                    data: [{dataArray}]
                }}]
            }},
            options: {{
                title: {{
                    display: true,
                    text: '{title}'
                }}
            }}
        }}";

        var encodedConfig = Uri.EscapeDataString(chartConfig);
        var imageUrl = $"https://quickchart.io/chart?c={encodedConfig}&w=500&h=300";

        // Geriye markdown image syntax döndürüyoruz
        var markdownImage = $"![{title}]({imageUrl})";
        
        return Task.FromResult(markdownImage);
    }
}
