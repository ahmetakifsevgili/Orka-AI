using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Kullanıcının yaş / eğitim seviyesi / hedef / üslup tercihini tek bir promt bloğuna dönüştürür.
/// Tüm ajan promptları buradan geçer — cümle uzunluğu, jargon yoğunluğu, analoji seçimi
/// ve müfredat derinliği kararı için tek kaynak.
/// </summary>
public class StudentProfileService : IStudentProfileService
{
    private readonly OrkaDbContext _db;

    public StudentProfileService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<string> BuildProfileBlockAsync(Guid userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user is null ? string.Empty : BuildProfileBlock(user);
    }

    public string BuildProfileBlock(User user)
    {
        // Hiç profil doldurulmamışsa prompt'u kirletme
        if (!user.Age.HasValue
            && user.EducationLevel == EducationLevel.Unknown
            && user.LearningGoal == LearningGoal.Unknown
            && user.LearningTone == LearningTone.Unknown
            && !user.DailyStudyMinutes.HasValue)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[ÖĞRENCİ PROFİLİ — PROMPT PERSONALİZASYON KURALI]");

        if (user.Age.HasValue)
            sb.AppendLine($"- Yaş: {user.Age.Value}");

        if (user.EducationLevel != EducationLevel.Unknown)
            sb.AppendLine($"- Eğitim Seviyesi: {DescribeEducation(user.EducationLevel)}");

        if (user.LearningGoal != LearningGoal.Unknown)
            sb.AppendLine($"- Öğrenme Amacı: {DescribeGoal(user.LearningGoal)}");

        if (user.LearningTone != LearningTone.Unknown)
            sb.AppendLine($"- Tercih Edilen Üslup: {DescribeTone(user.LearningTone)}");

        if (user.DailyStudyMinutes.HasValue)
            sb.AppendLine($"- Günlük Çalışma Süresi (tahmini): {user.DailyStudyMinutes.Value} dakika");

        sb.AppendLine();
        sb.AppendLine("UYGULAMA TALİMATI:");
        sb.AppendLine(AdaptationInstruction(user));

        return sb.ToString();
    }

    public (int MinLessons, int MaxLessons) SuggestLessonCountRange(User user, string topicCategory)
    {
        // Varsayılan (hiç profil yok) — mevcut davranışı koru
        int min = 8, max = 20;

        var edu = user.EducationLevel;
        var goal = user.LearningGoal;

        // Profesyonel / Akademik / ExamPrep hedefleri için derin plan
        bool deep =
            goal == LearningGoal.Academic
            || goal == LearningGoal.ExamPrep
            || goal == LearningGoal.Certification
            || edu == EducationLevel.Graduate
            || edu == EducationLevel.Professional;

        bool shallow =
            goal == LearningGoal.Hobby
            || edu == EducationLevel.Primary
            || edu == EducationLevel.Secondary
            || (user.Age.HasValue && user.Age.Value < 14);

        var cat = (topicCategory ?? string.Empty).ToLowerInvariant();
        bool programming = cat.Contains("prog") || cat.Contains("yazılım") || cat.Contains("kod") || cat.Contains("teknol") || cat.Contains("dev");
        bool exam = cat.Contains("sınav") || cat.Contains("kpss") || cat.Contains("yks") || cat.Contains("ales") || cat.Contains("exam");
        bool algorithm = cat.Contains("algoritma") || cat.Contains("veri yap") || cat.Contains("algorithm");

        if (deep || goal == LearningGoal.ExamPrep)
        {
            if (exam) { min = 80; max = 150; } // Devasa sınav müfredatı tetikleyicisi
            else if (programming || algorithm) { min = 25; max = 45; }
            else { min = 15; max = 28; }
        }
        else if (shallow)
        {
            min = 5; max = 10;
        }
        else
        {
            if (programming || algorithm) { min = 14; max = 24; }
            else if (exam) { min = 20; max = 35; } // Genel sınav incelemesi, Tiered Engine tetiklenmez
            else { min = 10; max = 18; }
        }

        // Günlük çalışma süresi sinyali varsa daralt
        if (user.DailyStudyMinutes.HasValue)
        {
            if (user.DailyStudyMinutes.Value < 20) { max = Math.Min(max, min + 4); }
            else if (user.DailyStudyMinutes.Value > 90) { min = Math.Max(min, max - 10); }
        }

        return (min, max);
    }

    public string BuildExamScaffolding(User user, string topicTitle)
    {
        if (user.LearningGoal != LearningGoal.ExamPrep) return string.Empty;
        if (string.IsNullOrWhiteSpace(topicTitle)) return string.Empty;

        var exam = DetectExamType(topicTitle);
        if (exam is null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"[SINAV ODAKLI MÜFREDAT — {exam.Code}]");
        sb.AppendLine($"Kullanıcı {exam.Code} ({exam.FullName}) sınavına hazırlanıyor. Müfredat bu sınavın resmi kapsamına uymalı.");
        sb.AppendLine();
        sb.AppendLine("ZORUNLU YAPI:");
        foreach (var rule in exam.Rules)
            sb.AppendLine($"- {rule}");
        sb.AppendLine();
        sb.AppendLine("DERS İSİMLENDİRME:");
        sb.AppendLine("- Ders başlıkları konu tarayıcısı gibi spesifik olmalı (örn: 'Paragrafta Ana Düşünce ve Anlatım Teknikleri').");
        sb.AppendLine("- Her modülün sonunda bir 'Geçmiş Yıl Soru Tipleri ve Tuzak Analizi' dersi bulunsun.");
        sb.AppendLine("- Son modülde 1-2 adet 'Tam Deneme Sınavı Stratejisi' dersi olmalı.");

        return sb.ToString();
    }

    private sealed record ExamType(string Code, string FullName, string[] Rules);

    private static ExamType? DetectExamType(string topicTitle)
    {
        var t = topicTitle.ToUpperInvariant();

        // YKS alt testleri önce kontrol edilir (TYT/AYT YKS'nin parçası)
        if (t.Contains("TYT")) return new ExamType("TYT",
            "Temel Yeterlilik Testi (YKS 1. Oturum)",
            new[] {
                "Temel: Türkçe, Matematik, Sosyal Bilimler, Fen Bilimleri dört ana başlık.",
                "Türkçe: Sözcükte/Cümlede Anlam, Paragraf, Dil Bilgisi alt modülleri.",
                "Matematik: Sayılar, Cebir, Geometri, Olasılık-Veri alt modülleri.",
                "Fen: Fizik (kinematik, elektrik), Kimya (mol, asit-baz), Biyoloji (hücre, ekosistem).",
                "Sosyal: Tarih (Osmanlı, Cumhuriyet), Coğrafya (iklim, nüfus), Felsefe, Din."
            });

        if (t.Contains("AYT")) return new ExamType("AYT",
            "Alan Yeterlilik Testi (YKS 2. Oturum)",
            new[] {
                "Sayısal: İleri Matematik (türev, integral, limit), Fizik (dinamik, dalgalar), Kimya (organik, çözeltiler), Biyoloji (genetik, sistemler).",
                "Eşit Ağırlık: Edebiyat, Tarih-1/2, Coğrafya-1/2, Matematik paylaşımlı.",
                "Sözel: Edebiyat ağırlıklı, Tarih (İnkılap dahil), Coğrafya, Felsefe Grubu (Psikoloji, Sosyoloji, Mantık).",
                "Dil: İngilizce (okuma anlama, çeviri, dil bilgisi) — ayrı oturum."
            });

        if (t.Contains("YKS")) return new ExamType("YKS",
            "Yükseköğretim Kurumları Sınavı",
            new[] {
                "Müfredat iki oturumu kapsamalı: TYT (temel) + AYT (alan).",
                "TYT: Türkçe, Matematik, Fen, Sosyal — temel seviye.",
                "AYT: Alan-özel (Sayısal / Eşit Ağırlık / Sözel / Dil) — ileri seviye.",
                "En az 2 modül TYT, 2 modül AYT odaklı olsun."
            });

        if (t.Contains("KPSS")) return new ExamType("KPSS",
            "Kamu Personeli Seçme Sınavı",
            new[] {
                "Genel Yetenek: Türkçe (sözcük, paragraf, dil bilgisi) + Matematik (sayısal mantık, problemler, geometri).",
                "Genel Kültür: Tarih (Osmanlı + İnkılap), Coğrafya (Türkiye), Vatandaşlık (Anayasa + güncel bilgiler).",
                "Eğitim Bilimleri (öğretmen adayları): Gelişim, Öğrenme, Program Geliştirme, Ölçme-Değerlendirme, Rehberlik.",
                "A grubu (Alan Bilgisi): Hukuk, İktisat, Maliye, Muhasebe, İşletme, Kamu Yönetimi, Çalışma Ekonomisi.",
                "En az 1 modül Genel Yetenek, 1 modül Genel Kültür + alan için ayrı modüller."
            });

        if (t.Contains("ÖABT") || t.Contains("OABT")) return new ExamType("ÖABT",
            "Öğretmenlik Alan Bilgisi Testi",
            new[] {
                "Branşa özel alan bilgisi (Matematik, Türkçe, Sınıf Öğretmenliği, Tarih, Coğrafya vb.) ağırlıklı.",
                "Alan eğitimi: branşın öğretim metodolojileri, program bilgisi, ölçme-değerlendirme.",
                "Tüm modüller o branşın MEB müfredatı kapsamını işlemeli."
            });

        if (t.Contains("ALES")) return new ExamType("ALES",
            "Akademik Personel ve Lisansüstü Eğitimi Giriş Sınavı",
            new[] {
                "Sözel Mantık: sözcük-anlam, cümle tamamlama, paragraf yorumu, mantık bulmacaları.",
                "Sayısal Mantık: problemler, sayı dizileri, geometri, olasılık, tablo-grafik yorumlama.",
                "Eşit ağırlıklı iki bölüm — modülleri Sözel ve Sayısal olarak denk böl."
            });

        if (t.Contains("DGS")) return new ExamType("DGS",
            "Dikey Geçiş Sınavı",
            new[] {
                "Sözel: Türkçe (anlam, paragraf, dil bilgisi), mantık.",
                "Sayısal: Matematik (cebir, geometri, olasılık), sayısal mantık.",
                "Önlisans programı ile hedef lisans arasındaki köprü konuları vurgula."
            });

        if (t.Contains("YDS") || t.Contains("YÖKDİL") || t.Contains("YOKDIL")) return new ExamType("YDS",
            "Yabancı Dil Sınavı",
            new[] {
                "Dilbilgisi (tenses, modals, clauses) ayrı modül.",
                "Kelime Bilgisi (akademik sözcük öbekleri, collocations) ayrı modül.",
                "Okuma-anlama (paragraf analizi, ana fikir, çıkarım) ağırlıklı.",
                "Cümle tamamlama, çeviri (Tr-İng / İng-Tr), diyalog tamamlama stratejileri."
            });

        return null;
    }

    private static string DescribeEducation(EducationLevel level) => level switch
    {
        EducationLevel.Primary      => "İlkokul",
        EducationLevel.Secondary    => "Ortaokul",
        EducationLevel.HighSchool   => "Lise",
        EducationLevel.University   => "Üniversite",
        EducationLevel.Graduate     => "Mezun / Yüksek lisans",
        EducationLevel.Professional => "Çalışan profesyonel / Doktora",
        _                           => "Bilinmiyor"
    };

    private static string DescribeGoal(LearningGoal goal) => goal switch
    {
        LearningGoal.ExamPrep      => "Sınav hazırlığı (KPSS/YKS/ALES vb.)",
        LearningGoal.Career        => "Meslek / kariyer geliştirme",
        LearningGoal.Hobby         => "Kişisel ilgi / hobi",
        LearningGoal.Academic      => "Akademik çalışma",
        LearningGoal.Certification => "Mesleki sertifika hazırlığı",
        _                          => "Genel öğrenme"
    };

    private static string DescribeTone(LearningTone tone) => tone switch
    {
        LearningTone.Formal   => "Akademik / resmi, jargon serbest",
        LearningTone.Friendly => "Sıcak fakat profesyonel",
        LearningTone.Playful  => "Oyunsu, bol analoji, kısa cümle",
        _                     => "Esnek"
    };

    private static string AdaptationInstruction(User user)
    {
        var age = user.Age ?? 0;
        var edu = user.EducationLevel;
        var goal = user.LearningGoal;
        var tone = user.LearningTone;

        // Yaş / eğitim bazlı temel kural
        if (age is > 0 and < 12 || edu == EducationLevel.Primary)
            return "Cümleler kısa (≤12 kelime). Teknik jargon YASAK. Günlük hayattan ve oyunlardan analoji kullan. Her paragraf sonunda 1 emojili teşvik.";

        if (age is >= 12 and < 15 || edu == EducationLevel.Secondary)
            return "Orta uzunlukta cümleler. Yabancı terimi kullanırken Türkçe karşılığını mutlaka paranteziçi ver. Günlük hayat örnekleri ağırlıkta.";

        if (age is >= 15 and < 19 || edu == EducationLevel.HighSchool)
            return "Teknik terim serbest ama ilk geçişte kısa tanım ver. Sınav adayıysa anahtar kavramları vurgula. Paragraf başına 1 özet cümle.";

        if (edu == EducationLevel.Professional || goal == LearningGoal.Certification || goal == LearningGoal.Academic)
            return "Derin teknik dil serbest. Sektör jargonu ve referans linkler kullan. Performans/ödünlerden (trade-off) açıkça bahset. Gereksiz analoji EKLEME.";

        if (tone == LearningTone.Playful)
            return "Kısa paragraflar, bol analoji, ara ara espri serbest. Ağır akademik ton YASAK.";

        if (tone == LearningTone.Formal)
            return "Akademik üslup. Kaynak referansları açık ver. Gereksiz analoji ve esprilerden kaçın.";

        return "Sıcak ama profesyonel üslup. Jargon kullanırken ilk geçişte kısa tanım ver. Her ana fikre 1 örnek eşlik etsin.";
    }
}
