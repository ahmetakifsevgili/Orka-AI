using System;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

/// <summary>
/// Kullanıcı profilini LLM prompt'una enjekte edilecek kısa bir blok olarak üretir.
/// DeepPlanAgent, TutorAgent, WikiAgent, KorteksAgent bu servisten tek tip profil
/// paragrafı alır — prompt promptun tutarlılığı için tek kaynak.
/// </summary>
public interface IStudentProfileService
{
    /// <summary>Verilen user id için hazır prompt bloğu döner. Profil yoksa boş string.</summary>
    Task<string> BuildProfileBlockAsync(Guid userId);

    /// <summary>Verilen user nesnesi için hazır prompt bloğu döner (DB'ye ek çağrı yapmaz).</summary>
    string BuildProfileBlock(User user);

    /// <summary>DeepPlan için ders sayısı önerisi (min, max) — yaş/hedef/çalışma süresine göre.</summary>
    (int MinLessons, int MaxLessons) SuggestLessonCountRange(User user, string topicCategory);

    /// <summary>
    /// Sınav hazırlığı kullanıcıları için müfredat iskelesi üretir. LearningGoal != ExamPrep
    /// ise veya sınav tipi tespit edilemezse boş string döner. Sınav adı topic başlığından
    /// çıkarılır (KPSS, YKS, TYT, AYT, ALES, DGS, YDS, ÖABT).
    /// </summary>
    string BuildExamScaffolding(User user, string topicTitle);
}
