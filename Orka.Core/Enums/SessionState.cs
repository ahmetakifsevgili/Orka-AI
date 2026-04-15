namespace Orka.Core.Enums;

public enum SessionState
{
    Learning,        // Tutor anlatıyor
    TopicCompleted,  // Analyzer "konu bitti" dedi
    QuizPending,     // Worker'lar (Summarizer/Quiz) bitti, Tutor sorma aşamasında
    QuizMode,        // Test çözülüyor
    AwaitingChoice,  // Kullanıcının Deep Plan veya Hızlı Sohbet seçmesi bekleniyor
    BaselineQuizMode // Deep Plan öncesi seviye tespit sınavı aşaması
}
