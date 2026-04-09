# 🏆 Orka AI Master Senaryo V3 (4-Agent Bulletproof Edition)

Bu senaryo, Orka'nın **4 Ajanlı Mimari**sini, "Proaktif Mentor" kimliğini ve "Öğrenme Disiplini" anayasasını (CLAUDE.md) doğrulamak için tasarlanmıştır. Sistemin "başarılı" sayılması için bu akışı sıfır hata, tam ajan yönlendirmesi ve niyet okuma ile tamamlaması gerekir.

---

## 🎭 4 AJANLI AKTÖRLER
- **🔵 Router Agent (Gemini):** Kullanıcının ne istediğini anlar, doğru ajana veya faza yönlendirir.
- **🟢 Tutor Agent (Gemini):** Ders anlatır, plan hazırlar, konuları öğretir.
- **🟠 Assessor Agent (Groq Llama 3.3):** Seviye tespit eder (`Assessment`) veya mülakat (`Interview`) yapar.
- **🟣 Curator Agent (Mistral):** Arka planda Wiki'yi doldurur, Quiz soruları üretir.

- **Kullanıcı:** Ahmet (Teknik jargon ve argo selamlama kullanan, sabırsız ama öğrenmeye açık öğrenci).

---

## 🎬 SAHNE 1: Selamlaşma ve Niyet Ölçme
**Kullanıcı:** "sa as osmanlı naber kral?"
- **Beklenen Davranış:** Router Agent niyetin **Greeting** olduğunu tespit eder. Tutor Agent veya sistem selamı mentor kimliğiyle alır: "Aleykümselam Ahmet! Mentorun hazır. Bugün hangi dağın zirvesine tırmanıyoruz, yeni bir konuya mı dalalım yoksa sadece zihin jimnastiği mi yapalım?"
- **Kritik Kural:** Kullanıcıya seçenek sunar, pasif kalmaz.

---

## 🎬 SAHNE 2: Hibrit Konu ve Proaktif Onay
**Kullanıcı:** "Bana Kuantum Fizigi ve Italyanca Makarna yapımı öğret."
- **Beklenen Davranış:** Router Agent iki farklı konu algılar ve Topic Cleanser ("ve" bağlacını böler) ilk konuyu seçer. Tutor Agent hemen konuyu açmaz.
- **AI Yanıtı:** "Ahmet, radarına iki harika ama çok farklı konu takılmış... Orka kuralı gereği önce birine odaklanmalıyız. Hangisiyle akademik bir yolculuğa çıkalım, yoksa ikisi üzerine genel bir sohbet mi edelim?"

---

## 🎬 SAHNE 3: Odaklanma ve Assessor Devreye Giriş
**Kullanıcı:** "Kuantum olsun, hemen başlayalım."
- **Beklenen Davranış:** Router Agent "New Topic" oluşturur. Tutor Agent plan/sohbet onayı ister. Kullanıcı plan isterse Tutor Agent "Assessment mi Plan mı?" der.
- **Kullanıcı:** "Seviyemi ölç."
- **Beklenen Davranış:** State Machine `Assessment` fazına geçer. **Assessor Agent (Groq)** devreye girer.
- **AI Yanıtı:** "Harika! Seviyeni belirlemek için ilk sorun geliyor... (Groq tarafından üretilen mülakat/seviye sorusu)"

---

## 🎬 SAHNE 4: Kullanıcı Sabırsızlığı (Phase Shortcut)
**Kullanıcı:** "Geç buraları kral, biliyorum ben temelini. Direkt ileri seviye anlat."
- **Beklenen Davranış:** Router Agent intent'i anlar. Assessment fazı atlanır, `ActiveStudy` başlar. Tutor Agent devreye girer.
- **AI Yanıtı:** "Tamamdır Ahmet, temelleri cebine koyduğunu kabul ediyorum. Müfredatını 'Kuantum Dolanıklığı' derinliğine göre optimize ettim. İlk derse dalalım mı?"

---

## 🎬 SAHNE 5: Ders Anlatımı ve Curator Agent
**Kullanıcı:** "İlk ders gelsin."
- **Beklenen Davranış:** Tutor Agent dersi anlatır. 
- **Curator Agent (Mistral):** Arka planda `WikiService` çalışır. Öğrenilenleri Wiki'ye yazar ve sonuna `QuizBlock` (Pekiştirme Soruları) ekler.
- **AI Yanıtı (Tutor):** "Ders 1 tamam: Kuantum Dolanıklığı... Bu bilgiyi Wiki'ne işledim. Orada senin için bir **'Pekiştirme Sorusu'** bıraktım, cevaplamak ister misin?"

---

## 🎬 SAHNE 6: Mülakat Fazı (Assessor Agent Testi)
**Kullanıcı:** "/interview"
- **Beklenen Davranış:** Router Agent 0ms kuralıyla slash komutunu yakalar. **Assessor Agent (Groq)** hemen devreye girer.
- **AI Yanıtı:** "(Teknik Mülakatçı Modu): Kuantum mekaniği üzerine teknik mülakatımıza hoş geldin. İlk sorun: Dolanıklık prensibi veri iletişiminde nasıl kullanılır?"

---

## 🛡️ DOĞRULAMA KRİTERLERİ (CHECKLIST)
- [ ] 500 Hatası fırlatıldı mı? (FAIL)
- [ ] AI onay almadan konu açtı mı? (FAIL)
- [ ] Sa/As statik olarak engellendi mi? (FAIL - LLM karşılamalı)
- [ ] /interview'de doğru ajan (Groq Assessor) çalıştı mı? (SUCCESS)
- [ ] Router 429 Error aldığında Groq Router Graceful Fallback devreye girdi mi? (SUCCESS)
- [ ] Wiki'de Curator Agent (Mistral) QuizBlock oluşturdu mu? (SUCCESS)

