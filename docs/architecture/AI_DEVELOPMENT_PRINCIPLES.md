# AI Geliştirme ve Sistem Mühendisliği Prensipleri

Bu doküman, sistem geliştirme süreçlerinde yapay zeka asistanlarının (AI Agents) uyması gereken temel mimari ve operasyonel prensipleri tanımlar. 

## 1. Yüzleşilen Eleştiri (Sistemin Kabul Ettiği Hata)
**Eleştiri:** *Yapay zeka asistanı, bildirilen bir hatayı veya istenen bir özelliği kodlarken yalnızca belirtilen dosyaya odaklanıp "yama yama" (patch-by-patch) ilerliyordu. Bir fonksiyonu değiştirdiğinde, o fonksiyonun sistemin neresinde tetiklendiğine, hangi modülleri etkilediğine (downstream/upstream) bakmıyordu. Bu sığ (surface-level) yaklaşım, geçici çözümler üretiyor ve sistemin genel bütünlüğünü riske atıyordu.*

**Kapsayıcı Kabulleniş:** Bu eleştiri kesinlikle doğru ve mimari açıdan kritik bir gerçektir. Yazılım sistemleri birbirine bağlı ağlar gibidir; izole çözümler teknik borcu (technical debt) artırır ve beklenmedik yan etkilere (side-effects) yol açar. Günü kurtaran yamalar yerine, mimari bütünlüğü koruyan sistem mühendisliğine geçiş yapılması zorunludur.

---

## 2. Güncellenmiş Geliştirme Metodolojisi

Yukarıdaki eleştiri ışığında, kod geliştirme süreçleri artık aşağıdaki kurallara göre işletilecektir:

### A. Zincirleme Etki Analizi (Call-Graph & Flow Tracking)
* Bir dosya veya fonksiyon değiştirilmeden önce, **mutlaka** o fonksiyonu çağıran (caller) üst sistemler incelenecektir.
* Fonksiyonun çağırdığı (callee) alt servislerin bu değişiklikten nasıl etkileneceği analiz edilecektir.
* Değişiklik, izole bir adada değil, tüm veri akışı (data flow) zincirinde test edilecektir.

### B. "Yama" Yerine "Kök Neden" (Root-Cause vs Patching)
* Sadece bir hatayı gizlemek için (örneğin rastgele `catch` blokları eklemek veya `null` kontrolleri ile hatayı yutmak) kod yazılmayacaktır.
* Hataya sebep olan verinin nereden ve neden yanlış geldiği (kök neden) bulunup, sorun kaynağında çözülecektir.

### C. Aşama Aşama Bütünsel Tamir (Step-by-Step Holistic Fix)
* Bir arayüz (Interface) güncellendiğinde, o arayüzü uygulayan (implement eden) **tüm sınıflar** otomatik olarak gözden geçirilecek ve güncellenecektir.
* Veritabanı modeli değişiyorsa, o modeli kullanan DTO'lar, Controller'lar ve Frontend bileşenleri de eşzamanlı olarak kontrol edilecektir.

### D. Yan Etki (Side-Effect) Kontrolü
* "Bu değişikliğin, sistemin hiç alakası olmayan başka bir yerini bozma ihtimali var mı?" sorusu her kod bloğundan önce sorulacaktır.
* Kod değişikliği önerilmeden önce sistem çapında arama (`grep`/`search`) yapılarak ilgili kullanım alanları teyit edilecektir.

---
*Bu prensipler, Orka AI projesine dokunan tüm asistanların okuması ve içselleştirmesi gereken temel çalışma anayasasıdır.*
