using Microsoft.Extensions.Logging;

namespace Orka.Infrastructure.Utilities;

/// <summary>
/// AI API çağrıları için diagnostik loglayıcı.
/// Ham JSON istek/yanıtlarını hem ILogger (structured) hem de dosyaya yazar.
/// Namespace: Orka.Infrastructure.Utilities (standart katmanlama)
/// </summary>
public static class AiDebugLogger
{
    private static readonly string LogDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Orka", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, "ai_debug.log");
    private static readonly string Sep     = new string('─', 80);

    public static void LogRequest(string provider, string content, ILogger? logger = null)
    {
        var msg = $"[{Now}] [{provider}] → REQUEST\n{content}\n{Sep}";
        logger?.LogDebug("[AI-DEBUG] {Provider} REQUEST: {Content}", provider, content);
        AppendSafe(msg);
    }

    public static void LogResponse(string provider, string content, ILogger? logger = null)
    {
        var msg = $"[{Now}] [{provider}] ← RESPONSE\n{content}\n{Sep}";
        logger?.LogDebug("[AI-DEBUG] {Provider} RESPONSE: {Content}", provider, content);
        AppendSafe(msg);
    }

    public static void LogError(string provider, string error, ILogger? logger = null)
    {
        var msg = $"[{Now}] [{provider}] ✗ ERROR: {error}\n{Sep}";
        logger?.LogWarning("[AI-DEBUG] {Provider} ERROR: {Error}", provider, error);
        AppendSafe(msg);
    }

    private static string Now => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static void AppendSafe(string content)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogFile, content + Environment.NewLine);
        }
        catch { /* Loglama asla uygulamayı kırmamalı */ }
    }
}
