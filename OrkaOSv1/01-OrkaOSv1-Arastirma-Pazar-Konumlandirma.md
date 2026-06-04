# OrkaOS v1 - Arastirma, Pazar ve Konumlandirma

Tarih: 2026-06-04  
Durum: OrkaOS v1 profesyonel kapanis dokumani  
Kapsam: Orka'nin mevcut sistemi, rakip/karsilastirma arastirmasi, hedef kitle, pazar konumu, gelecek urun firsatlari

## 1. Yonetici Ozeti

OrkaOS v1, klasik "chatbot" mantigindan ayrilip planlama, tutor, soru bankasi, wiki, kaynak uzerinden calisma, sesli calisma odasi, learning trace ve kalite kapilarini ayni ogrenme isletim sistemi icinde birlestiren yerli bir AI learning OS yaklasimidir.

Ana kararlar:

- Wiki ve OrkaLM ayri sistemler olarak kalir.
- Kaynak yukleme sadece OrkaLM'dedir.
- Wiki'deki ozellik katalogu OrkaLM'de de vardir, fakat veri/context karismaz.
- Iki sistem su an birbirini otomatik beslemez.
- Ses/audio en son fazda profesyonel seviyeye alinmistir.
- Strict AI rollerinde fake/in-memory cevap yoktur; kalite dusukse sistem fail-fast davranir.

Orka'nin farki tek bir "cevap ureten AI" olmamasi. Sistem, ogrencinin hedefini, kaynaklarini, soru cozumlerini, zayif kavramlarini, plan adimlarini, wiki notlarini ve kanit durumunu birlikte okuyarak ogrenme surecini yonetir.

## 2. Dis Pazar ve Rakip Arastirmasi

### 2.1 AI egitim pazari

AI destekli egitim pazari hizli buyuyor. Grand View Research, AI in education pazarini 2024'te 5.88 milyar USD olarak tahmin edip 2030'da 32.27 milyar USD'ye ulasabilecegini, 2025-2030 arasi %31.2 CAGR bekledigini belirtiyor. Mordor Intelligence ise 2025 icin 6.90 milyar USD, 2030 icin 41.01 milyar USD ve %42.83 CAGR tahmini veriyor. Bu rakamlar farkli metodolojilere sahip olsa da ortak sinyal net: personalized learning, intelligent tutoring, learning platforms ve virtual facilitators buyuyen ana segmentler.

Kaynaklar:

- Grand View Research: https://www.grandviewresearch.com/industry-analysis/artificial-intelligence-ai-education-market-report
- Mordor Intelligence: https://www.mordorintelligence.com/industry-reports/ai-in-education-market
- MarketsandMarkets/GlobeNewswire ozeti: https://www.globenewswire.com/news-release/2025/08/13/3132704/0/en/AI-in-Education-Market-Surges-to-5-82-billion-by-2030-Dominated-by-Microsoft-US-IBM-US-Google-US.html

### 2.2 NotebookLM referans noktasi

NotebookLM, kaynak odakli calisma icin en guclu referanslardan biri. Google Workspace sayfasinda NotebookLM'nin guvenilen kaynaklara dayali AI research/thinking partner olarak konumlandigi, Audio Overviews ve Mind Maps gibi ozellikleri destekledigi goruluyor. Google blog tarafinda Studio panelinin Audio Overview, Mind Map, Reports ve Video Overview gibi cok formatli uretimlere genisledigi belirtiliyor.

Orka icin cikarim:

- Source-grounded calisma artik kullanici beklentisi haline geldi.
- Tek output yetmiyor; kullanici briefing, study guide, quiz, flashcard, slide, diagram, audio gibi formatlar bekliyor.
- Audio tek basina TTS degil, diyalog formatinda pedagogik akistir.
- Kaynakli calismada citation, evidence status ve raw payload saklama kritik.

Kaynaklar:

- Google Workspace NotebookLM: https://workspace.google.com/products/notebooklm/
- Google Blog NotebookLM Studio upgrades: https://blog.google/technology/google-labs/notebooklm-video-overviews-studio-upgrades/
- DigitalOcean NotebookLM 2026 ozeti: https://www.digitalocean.com/resources/articles/what-is-notebooklm

### 2.3 Obsidian referans noktasi

Obsidian, knowledge vault dusuncesinde guclu referans. Obsidian Help, Graph View'in notlar arasindaki iliskileri node/edge olarak gorsellestirdigini; Canvas'in notlari, medyayi, PDF'leri, web sayfalarini ve baglantilari sonsuz 2D alanda duzenledigini; Properties'in frontmatter tabanli metadata mantigini destekledigini acikliyor.

Orka icin cikarim:

- Wiki sadece icerik sayfasi degil; linked knowledge system olmali.
- Backlinks, linked mentions, tags, properties, graph view ve block/reference mantigi Orka'nin ders akisi icin degerli.
- Fakat Orka, Obsidian gibi dis dosya/vault editoru olmak zorunda degil. Orka'nin avantaj noktasi, bu yapinin tutor, planlama ve soru bankasi ile bagli calismasi.

Kaynaklar:

- Obsidian Graph View: https://help.obsidian.md/plugins/graph
- Obsidian Canvas: https://obsidian.md/help/plugins/canvas
- Obsidian Properties: https://help.obsidian.md/properties
- Obsidian Bases/property syntax: https://obsidian.md/help/bases/syntax

## 3. Orka'nin Pazar Konumu

Orka, su dort sistem sinifinin kesisiminde konumlanir:

1. AI Tutor ve study coach
2. Source-grounded notebook
3. Linked knowledge wiki
4. Question bank ve assessment engine

Rakip tipleri:

- Genel AI chatbot: hizli cevap verir, fakat plan, kaynak, soru bankasi ve learning trace zayiftir.
- NotebookLM tipi kaynak defteri: kaynak uzerinden cok iyi calisir, fakat tam ders planlama, quiz evidence, soru bankasi ve uzun sureli mastery sistemi sinirlidir.
- Obsidian/Notion tipi bilgi defteri: not ve graph gucludur, fakat pedagogik tutor, diagnostic, adaptive quiz ve AI provider reliability katmani dogal olarak icinde degildir.
- LMS/kurumsal egitim platformu: icerik ve takip gucludur, fakat bireysel AI tutor deneyimi ve source notebook esnekligi zayif olabilir.

Orka'nin konumu:

> OrkaOS v1 = Tutor + Planlama + Soru Bankasi + Wiki + OrkaLM + Notebook Studio + Audio Study Room + Learning Trace.

## 4. Kimlere Hitap Ediyor?

### 4.1 Bireysel ogrenciler

Kullanim:

- Konu ogrenme
- Sinava hazirlik
- Zayif alan onarimi
- Kaynak PDF/slide/video notu uzerinden calisma
- Flashcard, quiz, study guide ve sesli tekrar

Deger:

- Ne calisacagini bilir.
- Zayif kavramlarini gorur.
- Kaynakla calisirken citation ve kanit durumunu gorur.
- Pasif okumayi aktif hatirlama ve quiz akisina cevirir.

### 4.2 Sinav hazirlik ogrencileri

Kullanim:

- KPSS/YKS/LGS/YDS benzeri sinav yollarinda konu planlama
- Diagnostic quiz
- Deneme/mini test
- Soru bankasi kalite sureci
- Yanlis cevap onarimi

Deger:

- Plansiz soru cozmek yerine kanitli zayifliklara gore calisir.
- Yanlis cevaplar "hata" olarak kalmaz; repair loop'a doner.

### 4.3 Yazilim ogrenenler

Kullanim:

- Kod hatasini ogrenme firsatina cevirme
- Algoritma/veri yapilari diagnostic
- UML, flowchart, sequence diagram
- IDE ciktisini tutor context'ine baglama

Deger:

- Compile/runtime hatalari learning signal olur.
- Konu, kod ve quiz ayni ogrenme trace'inde anlam kazanir.

### 4.4 Ogretmenler, tutorlar, icerik ekipleri

Kullanim:

- Ders notunu slide taslagina cevirme
- Checkpoint sorulari uretme
- Kavram haritasi/UML/mind map cikarma
- Soru bankasi kalite ve yayin sureci

Deger:

- Icerik uretimi hizlanir.
- Cikti "ham AI cevabi" degil, source/status/quality metadata tasir.

### 4.5 Kurumlar

Kullanim:

- Ogrenci learning trace
- Kaynakli egitim materyali
- Curriculum/standards mapping
- Assessment quality ve content operations

Deger:

- Olcekli egitimde kalite ve izlenebilirlik.
- Kaynak ve cevap guvenligi.

## 5. Urun Deger Onermesi

Orka'nin ana vaadi:

- "Sadece cevap verme; ogrencinin neyi bilmedigini bul, calisma yolunu kur, kaynaklarini bagla, sorularla olc, zayif kavrami onar ve bunu guvenli sekilde izle."

Destekleyici vaatler:

- Kaynakli calismada raw payload sizdirmaz.
- Wiki ve OrkaLM context'leri karismaz.
- Diagnostic ve planlama fake fallback ile makyajlanmaz.
- Sesli ozet/calisma odasi pedagogik metin, transcript ve caption ile gelir.
- Soru bankasi ve tutor ayni learning signal sistemiyle calisir.

## 6. Mevcut Sistem Durumu

Tamamlanan profesyonel kapilar:

- Auth token body hardening
- Public tutor trace sanitization
- Strict AI fallback kapatma
- Diagnostic question count heuristic
- Metadata-rich diagnostic blueprint contract
- DeepPlan fail-fast kalite kapisi
- Plan sequencing readiness duzeltmesi
- Chat quiz evidence synchronous record
- Audio context contract
- Wiki/OrkaLM upload ayrimi
- Wiki/OrkaLM feature parity browser kaniti
- Notebook Studio Faz 2-4 preview
- Faz 7 audio/caption/study room e2e kaniti

Son dogrulama:

- API full: 634/634
- Infrastructure full: 176/176
- Frontend typecheck/build/smoke: gecti
- Playwright full: 5 passed / 1 skipped
- Notebook Studio contract: 2/2

## 7. Stratejik Firsatlar

### 7.1 Kisa vade

- Life-proof skipped test'i aktif hale getirme
- Audio provider/Edge-TTS ortam stabilizasyonu
- Release packaging ve PR kapsam ayrimi
- Soru sayisi heuristic'ini pedagojik/urun olarak netlestirme

### 7.2 Orta vade

- OrkaLM -> Wiki manuel "send to wiki" akisi
- Wiki -> OrkaLM manuel "attach source" akisi
- Video overview / visual explainer
- Teacher dashboard
- Mobile-first sesli calisma

### 7.3 Uzun vade

- Kurumsal classroom analytics
- Standards/curriculum marketplace
- Ogrenci profilinden otomatik study path
- Multimodal source ingestion
- Local/offline knowledge vault export

## 8. Riskler ve Cevaplar

| Risk | Etki | Orka cevabi |
|---|---:|---|
| AI hallucination | Yuksek | Source grounding, citation, strict quality gate |
| Raw source leak | Yuksek | Public DTO sanitization, safe content |
| Fake progress | Yuksek | Learning signal ve quiz evidence zorunlu |
| Wiki/OrkaLM karismasi | Orta/Yuksek | Surface/context contract, e2e payload testi |
| TTS kalitesi | Orta | Backend TTS + browser fallback + transcript/caption |
| Soru sayisi pedagojisi | Orta | Heuristic, kalite gate, kullanici ile review |

## 9. Basari Metrikleri

Urun metrikleri:

- Diagnostic completion rate
- Study guide -> quiz conversion
- Wrong answer -> repair loop completion
- Source upload -> grounded answer rate
- Wiki page -> artifact generation rate
- Audio overview listen/ask rate

Kalite metrikleri:

- Citation support coverage
- Public raw payload leak count
- Strict provider failure transparency
- Thin plan rejection count
- Quiz evidence durability
- Cross-surface sync violation count

Gelir/Buyume metrikleri:

- Active learner retention
- Exam-prep cohort activation
- Source notebook usage per user
- Teacher/content workflow adoption
- Institutional pilot conversion

