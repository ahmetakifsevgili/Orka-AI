namespace Orka.Core.Enums;

/// <summary>
/// Kullanıcının tercih ettiği anlatım üslubu — yaş tek başına yeterli sinyal değil,
/// 20 yaşındaki bir tiyatrocu oyunsu dil, 15 yaşındaki bir YKS adayı ciddi dil isteyebilir.
/// TutorAgent / WikiAgent sistem promptlarında cümle uzunluğu, emoji, analoji seçimi için kullanılır.
/// </summary>
public enum LearningTone
{
    Unknown = 0,
    Formal = 1,    // Akademik, jargon serbest, referans linkli
    Friendly = 2,  // Sıcak fakat profesyonel — varsayılan
    Playful = 3    // Oyunsu, bol analoji, kısa cümleler, emoji serbest
}
