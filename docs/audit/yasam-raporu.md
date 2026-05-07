# Yaşam Raporu

## 1. Niyet Analizi

Durum: PASS_WITH_NOTE

Orka artık Plan Modu'nda ham kullanıcı mesajını doğrudan Korteks'e göndermiyor. İlk kapı `StudyIntentAnalyzer`.

Beklenen akış:

1. Kullanıcı ne çalışmak istediğini yazar.
2. Sistem ana alanı, odak konuyu, hedefi ve araştırılabilir niyeti ayırır.
3. Frontend kullanıcıya onay kartı gösterir.
4. Kullanıcı onaylamadan Korteks, quiz ve plan başlamaz.

Örnek:

- Kullanıcı: `java programlamada algoritmalar çalışmak istiyorum`
- Beklenen analiz: Java programlama / algoritmalar / öğrenme ve pratik / Java programming algorithms learning path

Puan: 8.5 / 10

Güçlü taraf:

- Analiz artık sistemde zorunlu ilk kapı.
- Fallback analiz Java + algoritmalar gibi yaygın ifadeleri ham konu olarak bırakmıyor.
- Model JSON dönerse yapılandırılmış analiz kullanılıyor.

Geliştirilecek taraf:

- Canlı model çıktılarıyla daha fazla Türkçe konu örneği test edilmeli.
- KPSS/YKS gibi sınav odaklarında niyet analizi ileride daha özel alt alan sözlüğü kullanabilir.

## 2. Onay UX'i

Durum: PASS

Frontend artık Plan Modu mesajından sonra niyet onay kartı gösteriyor.

Kartta görünenler:

- Ana alan
- Odak konu
- Çalışma amacı
- Korteks'e gidecek araştırma niyeti

Butonlar:

- Onayla ve araştır
- Düzelt
- Yeniden yaz

Puan: 8 / 10

Güçlü taraf:

- Kullanıcı artık sistemin ne anladığını görmeden araştırma başlatmıyor.
- "Düzelt" alanı analizi tekrar backend'e gönderiyor.
- "Yeniden yaz" pending niyeti temizliyor.

Geliştirilecek taraf:

- İleride kart daha görsel hale getirilebilir: konu kırılımı, kapsam uyarısı, tahmini quiz soru sayısı.
- Bu fazda shadcn ilkeleriyle sade kart düzeni korundu; büyük UI framework eklenmedi.

## 3. Düzeltme Akışı

Durum: PASS

Düzeltme girilince sistem bunu doğrudan Korteks'e iletmiyor. Tekrar `StudyIntentAnalyzer` çalışıyor ve yeni onay kartı üretiyor.

Kötü sonuç engeli:

- Düzeltme sonrası otomatik Korteks çağrısı yok.
- Onay gelmeden araştırma yok.
- Quiz yok.
- Plan yok.

Puan: 9 / 10

## 4. Korteks Çağrı Kapısı

Durum: PASS

`PlanDiagnosticService.StartAsync` artık `ApprovedResearchIntent` olmadan çalışmıyor. Bu backend seviyesinde güvenlik kapısıdır; frontend bypass edilse bile Korteks çağrısı başlamaz.

Kanıt:

- Unit test: onaylı niyet yoksa exception oluşur ve FakeKorteks çağrı sayısı 0 kalır.
- Controller bu durumu 500 yerine güvenli 400'e çevirir.

Puan: 9.5 / 10

## 5. Korteks Prompt Optimizasyonu

Durum: PASS_WITH_NOTE

Korteks'e artık ham kullanıcı cümlesi değil, onaylanmış araştırma niyeti gider.

Sistemsel İngilizce yönerge Korteks'ten şunları ister:

- learning route
- reliable web sources
- YouTube educational references
- prerequisites
- sub-concepts and hierarchy
- common misconceptions and beginner mistakes
- practice order and hands-on exercises
- Orka IDE/sandbox practice ideas when relevant

Korteks'e özellikle quiz veya final plan üretmemesi söylenir. Bu doğru mimari ayrımdır.

Puan: 8.5 / 10

Güçlü taraf:

- Araştırma görevi artık ansiklopedi özeti değil, öğrenme hazırlığı.
- Araştırma ile quiz/plan üretimi ayrıldı.

Geliştirilecek taraf:

- Canlı Korteks çıktıları kaynak kalitesi açısından örneklem bazında puanlanmalı.
- İleride kaynak skorlaması, YouTube süre/kalite filtresi ve kaynak çeşitliliği metriği eklenebilir.

## 6. Korteks Çıktısını Anlamlandıran Katman

Durum: PASS_WITH_NOTE

Mevcut `PlanResearchCompressor` ve `PlanIntelligenceBriefBuilder` hattı korunarak güçlendirildi. Yeni büyük servis açılmadı.

Sentez katmanının hedefi:

- Korteks çıktısını quiz kapsamına çevirmek
- Plan omurgası çıkarmak
- Ön koşul, alt kavram, pratik ve hata örüntülerini kullanılabilir hale getirmek
- Quiz sonucundan gelen bilinen / eksik ayrımını plana geçirmek

Puan: 8 / 10

Güçlü taraf:

- Mevcut mimari bozulmadı.
- Diagnostic summary artık `KnownConcepts`, `FastTrackConcepts`, `PracticeConcepts`, `WeakConcepts`, `MistakePatterns` taşıyor.

Geliştirilecek taraf:

- Korteks çıktısı çok zayıf gelirse sentez katmanı hâlâ kalite kapısına ihtiyaç duyar.
- Canlı örneklerde "anlamlı veriyi siliyor mu?" kontrolü için birkaç gerçek konu lifetest'i yapılmalı.

## 7. Quiz Kalitesi

Durum: PASS_WITH_NOTE

Quiz artık 15-25 soru hedefiyle üretiliyor. Soru sayısı konu kapsamı sinyallerine göre belirleniyor.

Soru sayısı mantığı:

- Dar konu: 15 civarı
- Orta konu: 18-20 civarı
- Geniş programlama / algoritma / framework kapsamı: 20-25 arası

Kalite kapısı şunları reddeder:

- İç sistem dili sızıntısı
- Generic pipeline soruları
- `input -> transform -> validate` gibi sahte kod blokları
- Java algoritmaları istenirken C# / .NET / Visual Studio sızıntısı
- Beklenen soru sayısından farklı quiz
- 15-25 dışı diagnostic quiz

Puan: 8 / 10

Güçlü taraf:

- Kötü fallback quiz artık kullanıcıya güzelmiş gibi gösterilmeyecek.
- Model kötü üretirse sistem sahte başarı yerine güvenli hata verir.

Geliştirilecek taraf:

- Gerçek seviye ölçme için her soru kavram etiketi, zorluk, beceri alanı ve yanılgı tipiyle daha zenginleştirilebilir.
- İleride adaptive next-question mantığı eklenebilir; bu fazda sabit diagnostic akış korundu.

## 8. Quiz Sonuç Analizi

Durum: PASS_WITH_NOTE

Quiz sonucunda sistem artık yalnızca doğru / yanlış sayısı yazmıyor. Şu ayrımlar plan brief'ine gider:

- Bilinen konular
- Hızlı geçilecek konular
- Pratiğe dökülecek konular
- Eksik veya hatalı konular
- Kavram yanılgıları

Gerçek seviye ölçme durumu:

- Doğru / yanlış oranı ölçülüyor.
- Yanlış cevaplardan zayıf kavramlar çıkarılıyor.
- Boş / skip durumları sahte yanlış gibi ele alınmamalı.

Puan: 8 / 10

Geliştirilecek taraf:

- Her yanlış cevabın neden yanlış olduğunu backend tarafında daha deterministik sınıflandırmak için ileride rubric tablosu eklenebilir.

## 9. Kişisel Plan

Durum: PASS_WITH_NOTE

Plan artık yalnızca üç alt başlıkla sınırlanacak şekilde tasarlanmıyor. Plan brief'i, araştırma ve quiz sonucundan gelen kavramları kullanacak şekilde güçlendirildi.

Beklenen plan davranışı:

- Bilinen konular hızlı tekrar + uygulama ile geçilir.
- Eksik konular mantıksal, detaylı, örnekli işlenir.
- Yazılım konularında Orka IDE/sandbox pratiği öne alınır.
- Plan, internetten bulunan araştırma ve kullanıcı seviyesine göre şekillenir.

Puan: 7.5 / 10

Güçlü taraf:

- Üç başlık gibi sert bir frontend kısıtı eklenmedi.
- Plan üretim brief'i kullanıcının quiz profiline göre daha net yönlendirildi.

Geliştirilecek taraf:

- Canlı plan çıktıları birkaç gerçek konuda elle puanlanmalı.
- Plan node'larının kalıcı graph/progress modeli ileride daha görünür hale getirilmeli.

## 10. Tutor Hafızası ve Davranışı

Durum: PASS_WITH_NOTE

Anlamlandırılmış quiz sonuçları plan intelligence brief içinde Tutor/plan hattına taşınıyor.

Tutor için doğru davranış:

- Bildiği konuyu gereksiz uzatmaz.
- Bildiği konuyu hızlı uygulama/pratikle pekiştirir.
- Eksik konuyu daha mantıksal, anlaşılır ve örnekli anlatır.
- Yazılımda Orka IDE/sandbox akışını merkeze alır.

Puan: 7.5 / 10

Geliştirilecek taraf:

- Tutor cevaplarında bu profilin ne kadar uygulandığı canlı konuşma lifetest'iyle ayrıca ölçülmeli.
- Öğrenme profilinin UI'da daha görünür olması V3 için iyi bir geliştirme alanı.

## 11. UX Dengesi

Durum: PASS

Bu fazda en kritik UX kararı alındı: quiz, plan ve sistem komutları chat mesajı gibi görünmeyecek. Chat Tutor cevabı için kalacak; plan/quiz ayrı öğrenme yüzeyi olarak ilerleyecek.

İyileştirilenler:

- Plan Mode aktif metni görünür ve anlamlı.
- Niyet onayı ayrı kart.
- Araştırma başlamadan önce kullanıcının kontrolü var.
- Düzeltme ayrı bir analiz turu.
- Quiz tek yüzeyde kalacak şekilde akış güvence altına alındı.
- Hata metinleri "sahte başarı" yerine güvenli ve dürüst.

Puan: 8.5 / 10

Geliştirilecek taraf:

- İleride niyet kartı daha profesyonel e-learning tasarımına çekilebilir.
- Plan ilerleme animasyonu gerçek backend event'leriyle daha zenginleştirilebilir.
- Quiz sonuç ekranı kavram haritası ve çalışma yolu olarak daha görsel hale getirilebilir.

## 12. Genel Sonuç

Yaşam Raporu kararı: ACCEPTED_WITH_NOTES

Bu patch sistemin ana zihinsel hattını doğru yere çekti:

Kullanıcı isteği -> niyet analizi -> kullanıcı onayı -> Korteks araştırması -> sentez -> quiz -> seviye analizi -> kişisel plan -> Tutor temposu.

En önemli kazanım:

Korteks artık kullanıcının ham cümlesiyle rastgele çalışmıyor. Onaylı, araştırılabilir niyetle ve öğrenme hazırlığı promptuyla çalışıyor.

Kalan ana not:

Bu mimari doğru kapıları koydu. Canlı ürün kalitesi için birkaç gerçek konu üzerinden Korteks çıktısı, quiz kalitesi ve plan kalitesi elle puanlanarak promptlar daha da keskinleştirilmeli.
