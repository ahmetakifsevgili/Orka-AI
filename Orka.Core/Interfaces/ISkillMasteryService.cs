using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface ISkillMasteryService
{
    /// <summary>
    /// Alt konu tamamlandığında mastery kaydeder (idempotent — aynı kayıt tekrar eklenemez).
    /// </summary>
    Task RecordMasteryAsync(Guid userId, Guid topicId, string subTopicTitle, int quizScore);

    /// <summary>
    /// Kullanıcının bir ana konu altındaki tüm mastery kayıtlarını döner.
    /// </summary>
    Task<IEnumerable<SkillMastery>> GetMasteriesByTopicAsync(Guid userId, Guid topicId);

    /// <summary>
    /// Kullanıcının tüm mastery kayıtlarını döner (profil sayfası için).
    /// </summary>
    Task<IEnumerable<SkillMastery>> GetAllMasteriesAsync(Guid userId);
}
