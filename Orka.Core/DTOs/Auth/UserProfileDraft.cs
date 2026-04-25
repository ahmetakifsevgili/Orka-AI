using Orka.Core.Enums;

namespace Orka.Core.DTOs.Auth;

/// <summary>
/// Register akışında ve Settings güncelleme akışında kullanılan profil paketi.
/// AuthService ve UserProfileController üzerinden User entity'sine kopyalanır.
/// </summary>
public class UserProfileDraft
{
    public int? Age { get; set; }
    public EducationLevel? EducationLevel { get; set; }
    public LearningGoal? LearningGoal { get; set; }
    public LearningTone? LearningTone { get; set; }
    public int? DailyStudyMinutes { get; set; }

    public bool HasAnyValue =>
        Age.HasValue
        || EducationLevel.HasValue
        || LearningGoal.HasValue
        || LearningTone.HasValue
        || DailyStudyMinutes.HasValue;
}
