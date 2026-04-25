namespace Orka.Core.Enums;

/// <summary>
/// Kullanıcının eğitim seviyesi — DeepPlanAgent ve TutorAgent promptlarında
/// cümle uzunluğu, jargon yoğunluğu ve örnek zorluk seviyesini ayarlamak için kullanılır.
/// </summary>
public enum EducationLevel
{
    Unknown = 0,
    Primary = 1,       // İlkokul (6-10 yaş)
    Secondary = 2,     // Ortaokul (11-13 yaş)
    HighSchool = 3,    // Lise (14-18 yaş) — YKS dahil
    University = 4,    // Üniversite öğrencisi — KPSS dahil
    Graduate = 5,      // Mezun / Yüksek lisans
    Professional = 6   // Çalışan profesyonel / Doktora
}
