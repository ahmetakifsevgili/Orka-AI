# Yasam Raporu

## 1. Niyet Analizi

Durum: PASS_WITH_NOTE

Orka Plan Modu'nda ham kullanici mesajini dogrudan Korteks'e gondermez. Ilk kapi `StudyIntentAnalyzer` katmanidir.

Beklenen akis:

1. Kullanici ne calismak istedigini yazar.
2. Sistem ana alani, odak konuyu, hedefi ve arastirilabilir niyeti ayirir.
3. Frontend kullaniciya onay karti gosterir.
4. Kullanici onaylamadan Korteks, quiz ve plan baslamaz.

Ornek:

- Kullanici: `java programlamada algoritmalar ve veri yapilari calismak istiyorum`
- Beklenen analiz: Java programlama / algoritmalar ve veri yapilari / ogrenme ve pratik / Java programming algorithms and data structures learning path

Puan: 9 / 10

Guclu taraf:

- Analiz sistemde zorunlu ilk kapi.
- Turkce karakterli isteklerde Java + algoritmalar + veri yapilari birlikte yakalaniyor.
- Duzeltme metni yeni bir niyet turu gibi ele aliniyor; otomatik Korteks'e gitmiyor.
- KPSS/YKS gibi sinav kisaltmalari korunuyor, sahte zayif alan uretilmiyor.

Gelistirilecek taraf:

- Canli model ciktilariyla daha fazla Turkce konu ornegi puanlanmali.
- Sinav odaklarinda ileride daha zengin alt alan sozlugu kullanilabilir.

## 2. Onay UX'i

Durum: PASS

Frontend Plan Modu mesajindan sonra niyet onay karti gosterir.

Kartta gorunenler:

- Ana alan
- Odak konu
- Calisma amaci
- Korteks'e gidecek arastirma niyeti
- "Onay yoksa arastirma, quiz ve plan baslamaz" guvenlik notu
- Niyet onayi -> Korteks arastirmasi -> 15-25 soru seviye testi -> kisisel plan adimlari

Butonlar:

- Onayla ve arastir
- Duzelt
- Yeniden yaz

Puan: 8.5 / 10

Guclu taraf:

- Kullanici sistemin ne anladigini gormeden arastirma baslatmaz.
- Duzelt alani analizi tekrar backend'e gonderir.
- Yeniden yaz pending niyeti temizler.
- Kart artik surecin tamamini daha anlasilir gosterir.

Gelistirilecek taraf:

- Ileride kart daha gorsel hale getirilebilir: konu kirilimi, kapsam uyarisi, tahmini quiz soru sayisi.

## 3. Duzeltme Akisi

Durum: PASS

Duzeltme girilince sistem bunu dogrudan Korteks'e iletmez. Tekrar `StudyIntentAnalyzer` calisir ve yeni onay karti uretir.

Kotu sonuc engeli:

- Duzeltme sonrasi otomatik Korteks cagrisi yok.
- Onay gelmeden arastirma yok.
- Quiz yok.
- Plan yok.

Puan: 9 / 10

## 4. Korteks Cagri Kapisi

Durum: PASS

`PlanDiagnosticService.StartAsync` `ApprovedResearchIntent` olmadan calismaz. Bu backend seviyesinde guvenlik kapisidir; frontend bypass edilse bile Korteks cagrisi baslamaz.

Kanit:

- Unit test: onayli niyet yoksa hata doner ve FakeKorteks cagri sayisi 0 kalir.
- Controller bu durumu 500 yerine guvenli 400'e cevirir.

Puan: 9.5 / 10

## 5. Korteks Prompt Optimizasyonu

Durum: PASS_WITH_NOTE

Korteks'e ham kullanici cumlesi degil, onaylanmis arastirma niyeti gider.

Sistemsel Ingilizce yonerge Korteks'ten sunlari ister:

- learning route
- reliable web sources
- YouTube educational references
- prerequisites
- sub-concepts and hierarchy
- common misconceptions and beginner mistakes
- practice order and hands-on exercises
- Orka IDE/sandbox practice ideas when relevant

Korteks'e quiz veya final plan uretmemesi soylenir. Bu dogru mimari ayrimdir.

Puan: 8.5 / 10

Guclu taraf:

- Arastirma gorevi ansiklopedi ozeti degil, ogrenme hazirligi.
- Arastirma ile quiz/plan uretimi ayrildi.

Gelistirilecek taraf:

- Canli Korteks ciktilari kaynak kalitesi acisindan orneklem bazinda puanlanmali.
- Ileride kaynak skorlamasi, YouTube sure/kalite filtresi ve kaynak cesitliligi metriği eklenebilir.

## 6. Korteks Ciktisini Anlamlandiran Katman

Durum: PASS_WITH_NOTE

Mevcut `PlanResearchCompressor` ve `PlanIntelligenceBriefBuilder` hatti korunarak guclendirildi. Yeni buyuk servis acilmadi.

Sentez katmaninin hedefi:

- Korteks ciktisini quiz kapsamina cevirmek
- Plan omurgasi cikarmak
- On kosul, alt kavram, pratik ve hata oruntulerini kullanilabilir hale getirmek
- Quiz sonucundan gelen bilinen / eksik ayrimini plana gecirmek
- `AccuracyPercent` ve `MeasuredLevel` sinyallerini plana tasimak

Puan: 8.5 / 10

Guclu taraf:

- Mevcut mimari bozulmadi.
- Diagnostic summary `KnownConcepts`, `FastTrackConcepts`, `PracticeConcepts`, `WeakConcepts`, `MistakePatterns`, `AccuracyPercent`, `MeasuredLevel` tasir.

Gelistirilecek taraf:

- Canli orneklerde "anlamli veriyi siliyor mu?" kontrolu icin birkac gercek konu lifetest'i yapilmali.

## 7. Quiz Kalitesi

Durum: PASS_WITH_NOTE

Quiz 15-25 soru hedefiyle uretilir. Soru sayisi konu kapsami sinyallerine gore belirlenir.

Soru sayisi mantigi:

- Dar konu: 15 civari
- Orta konu: 18-20 civari
- Genis programlama / algoritma / framework kapsami: 20-25 arasi

Kalite kapisi sunlari reddeder:

- Ic sistem dili sizintisi
- Generic pipeline sorulari
- `input -> transform -> validate` gibi sahte kod bloklari
- Java algoritmalari istenirken C# / .NET / Visual Studio sizintisi
- "Dogru secenek", "Yanlis secenek", "Correct option", "Wrong answer" gibi cevabi ele veren secenekler
- Beklenen soru sayisindan farkli quiz
- 15-25 disi diagnostic quiz

Puan: 8.5 / 10

Guclu taraf:

- Kotu fallback quiz kullaniciya guzelmis gibi gosterilmez.
- Model kotu uretirse sistem sahte basari yerine guvenli hata verir.
- Secenekler artik dogru/yanlis etiketini disari sizdiramaz.

Gelistirilecek taraf:

- Gercek seviye olcme icin her soru kavram etiketi, zorluk, beceri alani ve yanilgi tipiyle daha da zenginlestirilebilir.
- Ileride adaptive next-question mantigi eklenebilir; bu fazda sabit diagnostic akis korunur.

## 8. Quiz Sonuc Analizi

Durum: PASS_WITH_NOTE

Quiz sonucunda sistem yalnizca dogru / yanlis sayisi yazmaz. Su ayrimlar plan brief'ine gider:

- Bilinen konular
- Hizli gecilecek konular
- Pratige dokulecek konular
- Eksik veya hatali konular
- Kavram yanilgilari
- Dogruluk yuzdesi
- Olculen seviye

Gercek seviye olcme durumu:

- Dogru / yanlis orani olculur.
- Yanlis cevaplardan zayif kavramlar cikarilir.
- Bos / skip durumlari sahte yanlis gibi ele alinmamalidir.

Puan: 8.5 / 10

Gelistirilecek taraf:

- Her yanlis cevabin neden yanlis oldugunu backend tarafinda daha deterministik siniflandirmak icin ileride rubric tablosu eklenebilir.

## 9. Kisisel Plan

Durum: PASS_WITH_NOTE

Plan uretimi uc alt basliga dusen zayif fallback'e teslim edilmez. Domain fallback kalite zemini genisletildi: gerekli durumlarda en az 6 modul ve modul basina en az 4 ders olacak sekilde korunur.

Beklenen plan davranisi:

- Bilinen konular hizli tekrar + uygulama ile gecilir.
- Eksik konular mantiksal, detayli, ornekli islenir.
- Yazilim ve algoritma konularinda Orka IDE/sandbox pratigi one alinir.
- Plan, internetten bulunan arastirma ve kullanici seviyesine gore sekillenir.
- Algoritma/veri yapilari gibi konular generic "birinci temel kavram" listesine dusmez.

Puan: 8 / 10

Guclu taraf:

- Uc baslik gibi sert bir frontend kisiti yok.
- Plan kalite zemini backend fallback tarafinda da guclendirildi.
- Algoritma fallback testi Orka IDE ve alan kavramlarini korur.

Gelistirilecek taraf:

- Canli plan ciktilari birkac gercek konuda elle puanlanmali.
- Plan node'larinin kalici graph/progress modeli ileride daha gorunur hale getirilmeli.

## 10. Tutor Hafizasi ve Davranisi

Durum: PASS_WITH_NOTE

Anlamlandirilmis quiz sonuclari plan intelligence brief icinde Tutor/plan hattina tasinir.

Tutor icin dogru davranis:

- Bildigi konuyu gereksiz uzatmaz.
- Bildigi konuyu hizli uygulama/pratikle pekistirir.
- Eksik konuyu daha mantiksal, anlasilir ve ornekli anlatir.
- Yazilimda Orka IDE/sandbox akisini merkeze alir.

Puan: 7.5 / 10

Gelistirilecek taraf:

- Tutor cevaplarinda bu profilin ne kadar uygulandigi canli konusma lifetest'iyle ayrica olculmeli.
- Ogrenme profilinin UI'da daha gorunur olmasi V3 icin iyi bir gelistirme alanidir.

## 11. UX Dengesi

Durum: PASS

En kritik UX karari korunuyor: quiz, plan ve sistem komutlari chat mesaji gibi gorunmez. Chat Tutor cevabi icin kalir; plan/quiz ayri ogrenme yuzeyi olarak ilerler.

Iyilestirilenler:

- Plan Mode aktif metni gorunur ve anlamli.
- Niyet onayi ayri kart.
- Arastirma baslamadan once kullanicinin kontrolu var.
- Duzeltme ayri bir analiz turu.
- Niyet kartinda tum akis adimlari gorunur.
- Quiz tek yuzeyde kalacak sekilde akis guvence altina alindi.
- Hata metinleri "sahte basari" yerine guvenli ve durust.

Puan: 9 / 10

Gelistirilecek taraf:

- Plan ilerleme animasyonu gercek backend event'leriyle daha zenginlesebilir.
- Quiz sonuc ekrani kavram haritasi ve calisma yolu olarak daha gorsel hale getirilebilir.

## 12. Genel Sonuc

Yasam Raporu karari: ACCEPTED_WITH_NOTES

Bu patch sistemin ana zihinsel hattini dogru yere cekti:

Kullanici istegi -> niyet analizi -> kullanici onayi -> Korteks arastirmasi -> sentez -> quiz -> seviye analizi -> kisisel plan -> Tutor temposu.

En onemli kazanim:

Korteks kullanicinin ham cumlesiyle rastgele calismaz. Onayli, arastirilabilir niyetle ve ogrenme hazirligi promptuyla calisir.

Kalan ana not:

Mimari dogru kapilari koydu. Canli urun kalitesi icin birkac gercek konu uzerinden Korteks ciktisi, quiz kalitesi ve plan kalitesi elle puanlanarak promptlar daha da keskinlestirilmelidir.

## 13. Uc Senaryolu Lifetest Guncellemesi

Durum: PASS

Bu turda canli urun sikayetlerinden cikan en kritik risk tekrar test seviyesine alindi: sistem tek bir C# senaryosuna kilitleniyor mu, yoksa farkli niyetleri dogru ayirip Korteks/quiz/plan hattina temiz tasiyor mu?

Test edilen senaryolar:

1. `java programlamada algoritmalar ve veri yapilari calismak istiyorum`
2. `KPSS paragraf sorularinda hizlanmak istiyorum`
3. `SQL veritabani indeksleri ve sorgu optimizasyonu calismak istiyorum`

Kanıt:

- Java senaryosu `Java programming algorithms and data structures learning path` niyetine dondu ve 25 soruluk diagnostic kapsam uretildi.
- KPSS senaryosu `KPSS paragraph questions speed practice learning path` niyetine dondu ve odağa gereksiz KPSS tekrari basilmadi.
- SQL senaryosu `SQL programming database indexes and query optimization learning path` niyetine dondu ve odaktan gereksiz `SQL` tekrari temizlendi.
- Her uc senaryoda Korteks'e ham kullanici cumlesi degil, onaylanmis arastirma niyeti gitti.
- Her uc senaryoda quiz 15-25 araliginda kaldi.
- Her uc senaryoda diagnostic summary `KnownConcepts`, `WeakConcepts`, `AccuracyPercent` ve `MeasuredLevel` tasidi.

Sonuc:

Bu kisim artik sadece dokuman notu degil; backend unit test olarak kalici regresyon kapisi haline getirildi.

## 14. OrkaLM / Wiki Ayrimi

Durum: PASS

Wiki ozelligi aktif tutuldu. Wiki, konu haritasi ve ders hafizasi yuzeyi olarak kalmaya devam eder.

Yeni `OrkaLM` sol bara eklendi. OrkaLM, mevcut NotebookLM benzeri kaynak zekasini ayri ve daha dogru isimlendirilmis bir yuzeyde toplar:

- PDF / TXT / MD kaynak yukleme
- kaynak grafigi
- kaynak kanit paneli
- sayfa/chunk eslesmesi
- ozet/briefing
- terimler
- zihin haritasi
- pekistirme kartlari
- audio overview

Onemli karar:

Bu fazda yeni bir backend sistemi icat edilmedi. Mevcut kaynak/wiki/audio altyapisi korunarak OrkaLM yuzeyine enjekte edildi. Daha derin NotebookLM optimizasyonlari V3 icin ayrildi.
