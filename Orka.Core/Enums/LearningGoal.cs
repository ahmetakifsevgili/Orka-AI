namespace Orka.Core.Enums;

/// <summary>
/// Kullanıcının öğrenme amacı — müfredat derinliği, quiz zorluğu ve
/// konu öneri stratejisini ayarlamak için kullanılır.
/// </summary>
public enum LearningGoal
{
    Unknown = 0,
    ExamPrep = 1,       // KPSS, YKS, ALES, sertifika sınavı gibi zamanlı hedefler
    Career = 2,         // Meslek değişikliği, iş için beceri
    Hobby = 3,          // Kişisel ilgi, rahat tempo
    Academic = 4,       // Tez, akademik proje, derin teorik ihtiyaç
    Certification = 5   // Mesleki sertifika (AWS, Azure, PMP vb.)
}
