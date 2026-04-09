# Orka Dynamic Orchestration Roadmap

Bu dosya, Orka AI'nın "Dinamik Diyalog Yöneticisi" (Dialogue Manager) olarak nasıl çalışması gerektiğini tanımlar. Statik filtreleme ve basit switch-case mantığı bu projede YASAKTIR.

## 🎯 Hedef: "Akıllı Mentor" Deneyimi
Kullanıcı hiçbir butona basmadan, sadece yazarak; AI'nın süreci (Seviye tespiti, Planlama, Ders anlatımı) yönettiği bir akış kurmaktır.

---

## 🏗️ Mimari Katmanlar

### 1. Diyalog Katmanı (Dialogue Management)
- **Hafıza (DB Metadata):** AI her mesajda Topic.TopicData alanına bakarak kullanıcının geçmişini hatırlar.
- **Niyet Arama (Intent Discovery):** Sadece "ne dediğine" değil, "neyi amaçladığına" bakar. (Sohbet mi? Öğrenme mi? Mülakat mı?)
- **Proaktiflik:** "C öğrenmek istiyorum" diyene pat diye plan üretmez. Önce "Plan mı yapalım sohbet mi?" diye sorar.

### 2. Ajanlar ve Görevler (Agents & Actions)
- **Manager (ChatService):** Süreci yöneten ana beyin.
- **Assessor (AssessmentAgent):** Kullanıcının seviyesini 3-5 soruyla ölçen uzman.
- **Planner (TopicService):** Seviye verisine göre kişiselleştirilmiş müfredat sentezleyen uzman.
- **Tutor (StudyAgent):** Ders anlatan, Wiki'yi besleyen ve sonunda "Pekiştirme Soruları" hazırlayan uzman.

---

## 🛠️ Uygulama Adımları

### ADIM 1: Veritabanı Evrimi
Topic tablosuna CurrentPhase (Enum) ve PhaseMetadata (JSON) alanları ekle. Bu alanlar "Seviye ölçüyor muyuz?", "Plan bekliyor muyuz?" gibi durumları tutacak.

### ADIM 2: ChatService (The Brain) Refaktörü
ProcessMessageAsync metodunu bir "State Machine" haline getir:
- Kullanıcı mesajı + Geçmiş + Metadata -> AI ANALİZİ
- Karar: "Diyaloğa devam et", "Seviye soruları sor", "Planı üret", "Dersi anlat".

### ADIM 3: Wiki'yi Canlandırma
Wiki sayfalarının altına QuizBlock tipinde "Pekiştirme Soruları" (Reinforcement Questions) ekle. Ders bittiğinde kullanıcıyı buraya yönlendir.

---

## 🚨 KRİTİK UYARILAR
- "sa", "as", "osmanlı" gibi kelimelerde sapıtan filtrelemeleri tamamen kaldır.
- Kullanıcıya "Hazır mısın?", "Başlayalım mı?", "Seviyeni ölçeyim mi?" gibi sorular sormadan eylem yapma.
- Kullanıcıyı bir konudan diğerine geçerken "Eski konuyu kaydedip buna mı geçelim?" diyerek onayla.

## ✅ DOĞRULAMA (TEST)
- [ ] "Merhaba" dendiğinde eğer aktif konu varsa "Hoşgeldin, X'e devam mı?" diyor mu?
- [ ] Yeni bir konu söylendiğinde "Plan mı sohbet mi?" diye soruyor mu?
- [ ] Seviye tespiti yapıp, plana bu veriyi yansıtıyor mu?
- [ ] Wiki'de "Pekiştirme Soruları" çıkıyor mu?
