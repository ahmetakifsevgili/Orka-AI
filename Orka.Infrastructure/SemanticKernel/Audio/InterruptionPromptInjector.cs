using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Orka.Infrastructure.SemanticKernel.Audio;

/// <summary>
/// Faz 4: Dynamic Persona Injection 
/// Öğrenci sesi/dersi kestiğinde LLM'in ChatHistory geçmişini
/// dinamik olarak manipüle edip "Toparlanma (Recovery)" kuralını enjekte eder.
/// </summary>
public static class InterruptionPromptInjector
{
    public static void InjectInterruptionEvent(ChatHistory history, string truncatedTutorSentence, string studentQuestion, string studentPersonaContext = "", bool isVoiceMode = false)
    {
         // 1. Hocanın/Asistanın yarıda kalan cümlesini özel bir ibareyle kaydediyoruz
         history.AddAssistantMessage($"{truncatedTutorSentence}... [ÖĞRENCİ SÖZÜNÜ KESTİ]");

         // 2. Sistemin kafasının karışmaması için "O andaki" duruma özel gizli sistem mesajı atıyoruz
         string recoveryDirective;
         
         if (isVoiceMode)
         {
             recoveryDirective = 
                $"[SİSTEM DİREKTİFİ - PODCAST KESİNTİSİ]: Sen ve Asistan (Emel) radyo programı (Sesli Sınıf) konseptinde dersi anlatırken tam o anda " +
                $"sözünüz kesildi ve öğrenci araya girip bir soru sordu.\n" +
                $"[ÖĞRENCİ PROFİLİ]: {studentPersonaContext}\n" +
                $"GÖREVİN: Robotik tepkiler kesinlikle verme. Bir podcast kesintisi gibi, Asistan veya Hoca hemen devreye girip öğrencinin sözünü onaylasın. " +
                $"(Örn: [ASISTAN]: 'Araya girdin harika oldu, Hoca tam oraya gelecekti değil mi hocam?' VEYA [HOCA]: 'Güzel nokta, hemen açıklayayım') " +
                $"doğal bir geçişle sorusunu yanıtlayın ve muhabbeti kaldığı yerden paslaşarak sürdürün. Daima [HOCA]: ve [ASISTAN]: taglarını kullanmaya devam edin.";
         }
         else
         {
             recoveryDirective = 
                $"[SİSTEM DİREKTİFİ]: Sen Orka Tutor ajansın. Yukarıda dersi anlatırken tam cümlenin ortasında " +
                $"sözün kesildi ve öğrenci araya girip bir soru sordu.\n" +
                $"[ÖĞRENCİ PROFİLİ (Redis'ten)]: {studentPersonaContext}\n" +
                $"GÖREVİN: Robotik tepkiler verme! Tamamen insansı bir şekilde, kaba olmadan " +
                $"(Örn: 'Haklısın oraya hemen değineyim', veya 'Çok güzel bir yere parmak bastın') " +
                $"gibi doğal bir geçişle sorusunu yanıtla ve SEZİSİZCE bir önceki anlattığın plana geri dönüp dersi sürdür. " +
                $"Tepkinin tonunu kesinlikle Öğrenci Profiline uygun ayarla.";
         }
            
         history.AddSystemMessage(recoveryDirective);

         // 3. Öğrencinin böldüğü anki mikrofon sorusu
         history.AddUserMessage(studentQuestion);
    }
}
