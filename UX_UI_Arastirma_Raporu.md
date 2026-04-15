# Orka AI: UX/UI Derin Araştırma ve Analiz Raporu

Bu rapor, paylaşılan referans görselleri (Resim 5 ve 6) ile mevcut sistem arasındaki farkları analiz etmek ve platformun premium hissiyatını güçlendirecek çözüm önerilerini sunmak amacıyla hazırlanmıştır.

---

## 📂 1. Yan Panel Hiyerarşisi (Sidebar Branching) Analizi

### Mevcut Durum:
Mevcut `LeftSidebar.tsx` bileşeni, konuları iki ana grupta toplamaktadır: "Sohbet Geçmişi" ve "Öğrenme Programları". Ancak şu anki mantıkta bir konunun "Program" olarak görünebilmesi için sistemin bu konunun altında halihazırda çocuk (sub-topic) konular bulması gerekmektedir. Eğer yeni bir plan oluşturulursa ancak henüz alt başlıklar üretilmemişse, bu plan ana listede sıradan bir sohbet gibi görünmektedir.

### Referans Görsel (Resim 6) vs. Mevcut Durum:
*   **Dallanma (Tree Structure)**: Referans görselde, ana konuların yanında bir ok (chevron) bulunmakta ve tıklandığında altındaki müfredat (curriculum) dallanarak açılmaktadır.
*   **Görsel Bağlantılar**: Alt başlıkların ana başlığa bağlı olduğunu gösteren ince "konnektör çizgileri" şu anki sistemimizde eksiktir.
*   **Gruplama**: Kullanıcı, "Çalışma Programları" altında net bir hiyerarşi görmeyi beklemektedir.

### Önerilen Teknik Çözüm:
*   `Topic` varlığına `Category="Plan"` alanı zorunlu kılınarak, çocuk konusu olmasa dahi her zaman "Öğrenme Programları" altında listelenmesi sağlanacaktır.
*   Framer Motion kullanılarak, dallanma açıldığında (accordion) konnektör çizgilerinin de yumuşak bir şekilde belirmesi sağlanacaktır.

---

## 📖 2. Wiki Çekmecesi (Interactivity) Analizi

### Mevcut Durum:
`WikiDrawer.tsx`, sağdan kayan ve sabit `450px` genişliğe sahip bir paneldir. Boyutları statiktir ve kullanıcı tarafından değiştirilemez.

### Referans Görsel (Resim 5) vs. Mevcut Durum:
*   **Boyut ve Okunabilirlik**: Referans görselde Wiki alanı, ekranın büyük bir kısmını kaplayabilen, geniş ve ferah bir döküman yapısına sahiptir. Tablolar ve kod blokları geniş alanda çok daha okunabilirdir.
*   **Zengin Deneyim**: Mevcut panel "dar" kaldığı için karmaşık tablolar (Resim 5'teki Paradigma tablosu gibi) sıkışık görünebilir.

### Önerilen Teknik Çözüm:
*   **Resizable Divider**: Panel ile ana içerik arasına bir "sürükleyici" (drag handle) eklenecektir. Kullanıcı fare ile paneli genişletip daraltabilecektir.
*   **Adaptive prose**: Genişlik arttıkça font boyutlarının ve boşlukların (padding) dinamik olarak ayarlanması (`max-w-none` optimizasyonu) sağlanacaktır.

---

## 🛠️ 3. Teknik Kök Nedenler ve Mimari Hazırlık

1.  **Veri Modeli Uygunluğu**: `Topic` tablosu `ParentTopicId` ve `Order` alanlarına sahiptir. Yani mimari, hiyerarşik dallanmayı desteklemektedir. Sorun sadece UI'daki filtreleme ve görselleştirme katmanındadır.
2.  **Plugin Mantığı**: `DeepPlanAgent.cs` planları oluştururken parent-child ilişkisini doğru kurmaktadır ancak UI'da bu ilişkiyi "her zaman" göstermiyoruz.
3.  **Layout Kısıtları**: React tarafındaki `MainLayout` yapısı şu an yan panel ve ana içerik için sabit flex oranları kullanmaktadır. Resizing için bu yapının `ResizablePanelGroup` benzeri bir mantığa taşınması gerekmektedir.

---

## 🏁 Sonuç ve Onay Beklentisi

Araştırma sonucunda sistemin altyapısının bu değişikliklere hazır olduğu, sadece kullanıcı tarafındaki görsel motorun (Sidebar ve Drawer) modernize edilmesi gerektiği tespit edilmiştir. 

**Değişiklik yapılmamış, onayınız beklenmektedir.**

---
*Analist: Antigravity AI*
*Tarih: 11 Nisan 2026*
