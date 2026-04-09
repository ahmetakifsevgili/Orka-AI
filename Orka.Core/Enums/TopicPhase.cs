namespace Orka.Core.Enums;

public enum TopicPhase
{
    /// <summary>
    /// Kullanıcının niyetinin ve konusunun belirlendiği ilk aşama.
    /// </summary>
    Discovery = 0,

    /// <summary>
    /// Kullanıcının seviyesinin (Beginner/Intermediate/Advanced) ölçüldüğü aşama.
    /// </summary>
    Assessment = 1,

    /// <summary>
    /// Seviyeye özel müfredatın (Plan) oluşturulduğu ve kullanıcı onayının beklendiği aşama.
    /// </summary>
    Planning = 2,

    /// <summary>
    /// Aktif ders anlatımı ve interaktif çalışmanın yapıldığı ana aşama.
    /// </summary>
    ActiveStudy = 3,

    /// <summary>
    /// Konunun başarıyla tamamlandığı aşama.
    /// </summary>
    Completed = 4
}
