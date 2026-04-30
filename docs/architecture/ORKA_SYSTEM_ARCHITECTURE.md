# 🔱 Orka AI: Konsolide ve Parçalı Sistem Mimarisi (Final State)

Bu belge, Orka AI projesinin bütününe ve her bir modülün iç işleyişine odaklanan nihai teknik anayasadır.

---

## 🏗️ 1. GLOBAL HİBRİT MİMARİ (Full Stack Flowchart)
*Büyük Resim: Tüm ajanların, servislerin ve veri yollarının kesiştiği ana harita.*

```mermaid
graph TD
    %% Katmanlar
    subgraph "Arayüz ve Kullanıcı"
        U["👤 Öğrenci / Sentetik Persona"] --> IDE["💻 Interactive IDE & Chat UI"]
    end

    subgraph "Zeka ve Rotalama Merkezi"
        IDE --> SA["🤖 SupervisorAgent (Orchestrator)"]
        SA --> ICA["🧠 IntentClassifierAgent"]
        ICA -.->|"Niyet Kütüphanesi & Duygu Analizi"| ICA
        ICA --"{intent, confidence}"--> SA
    end

    subgraph "Müfredat ve Planlama (Curriculum Builder)"
        SA --"PLAN_MODE"--> DPA["📋 DeepPlanAgent"]
        DPA -->|"Dinamik Yol Haritası Üret"| DB_SES[(Database: Session/Curriculum)]
        DPA -.->|"Seviye Kontrolü"| G
    end

    subgraph "Bilgi Havuzu ve Araştırma (Wiki/RAG)"
        SA --"RESEARCH"--> KA["🔍 KorteksAgent"]
        KA -->|"Anlamsal Arama (Cohere)"| VDB[("🗄️ Vektör DB / Knowledge Base")]
        SA --"WIKI_QUERY"--> WA["📖 WikiAgent"]
        WA -->|"Özetle ve Açıkla"| G
    end

    subgraph "Üretim ve Kod Çalıştırma (Tutor/IDE)"
        SA --"TUTOR"--> TA["📝 TutorAgent"]
        SA --"QUIZ"--> QA["❓ QuizAgent"]
        IDE --"Derle"--> PS["⚙️ PistonService"]
        PS --"Çalıştıma Sonucu"--> TA
    end

    subgraph "Denetim ve Konsensüs (Peer Review)"
        TA & QA & WA & DPA -.->|"İçerik Onayı"| G["🛡️ GraderAgent"]
        G -.->|"Oylama: Llama-8B vs Gemini"| CONS["⚖️ Consensus"]
        CONS --"Çelişki"--> L405["💎 Llama-405B"]
        G --"ONAY"--> IDE
    end

    subgraph "Öğrenme ve Evrim Döngüsü (Sinir Sistemi)"
        TA & IDE -.->|"Trace Data"| EA["💹 EvaluatorAgent"]
        EA --"A:Pedagoji / B:Doğruluk / C:Niyet"--> R[("📢 Redis: Muhabbir")]
        
        %% Geliştirici Katmanı
        DEV["👤 Geliştirici (Siz)"] -->|"Onayla/Mühürle"| R
        
        R -.->|"Hatalar Defteri & Prompt Tuning"| TA & SA & DPA
        R --> GRA["📊 Grafana / LLMOps Dashboard"]
    end

    subgraph "Orka Sandbox (Otonom Simülasyon)"
        SB["🏭 Sandbox Engine"] -->|"Persona A/B/C"| U
        R -.->|"Zayıf Senaryoları Test Et"| SB
        SB -.->|"Adversarial Evolution (A+)"| U
    end
```

---

## 💬 2. MESAJIN TEKNİK YOLCULUĞU (Sequence Diagram)
*Bir isteğin (Ders, Soru veya Plan) ajanlar arasındaki anlık fısıldaşmaları.*

```mermaid
sequenceDiagram
    participant U as Ogrenci
    participant S as Supervisor (Plan/Chat)
    participant T as Tutor/Plan/Wiki Agents
    participant G as Grader (Denetim)
    participant E as Evaluator (Puanlama)
    participant R as Redis (Muhabbir)

    U->>S: "Sıradaki Konu?" / "Kodu Çalıştır"
    S->>T: Uzman Ajanı Görevlendir (Tutor/Plan/Wiki)
    
    rect #E6FFE6
    Note over T,G: Peer Review (Denetim)
    T->>G: Taslak İçerik (Ders/Plan/Soru)
    G-->>T: Onay (Consensus Destekli)
    end
    
    T-->>U: Mükemmel Cevap (Stream)
    
    rect #E8E8E8
    Note over U,R: Puanlama ve Hafıza Kaydı
    U->>E: Yanıt Çiftini Analiz Et
    E->>R: [Pedagoji, Doğruluk, Niyet] -> Redis
    end
    
    rect #E1F5FE
    Note over R,T: Öğrenme ve Adaptasyon
    R->>T: "Öğrenci anladı, derinliği artır" (Hatalar Defteri)
    end
```

---

## 📋 3. MÜFREDAT VE PLANLAMA (DeepPlan) AKIŞI
*Sistemin bir konuyu nasıl parçalara ayırdığı ve öğrenciden onay aldığı süreç.*

```mermaid
graph LR
    SA["Supervisor"] -->|"Plan İsteği"| DPA["DeepPlanAgent"]
    DPA -->|"Seviye Analizi"| GR["Grade Checker"]
    GR -->|"7. Sınıf Seviyesi"| DPA
    DPA -->|"Ders Modülleri & Topic Mapping"| DB[(Database)]
    DB -->|"Öğrenciye Taslak Sun"| U["👤 Öğrenci"]
    U --"Uygundur"--> DPA
    DPA --"Ok, Dersleri Başlat"--> SA
```

---

## 📖 4. BİLGİ HAVUZU VE WİWİ ENTEGRASYONU
*RAG (Retrieval-Augmented Generation) katmanının çalışma mantığı.*

```mermaid
graph TD
    SA["Supervisor"] -->|"Detaylı Bilgi Lazım"| KA["KorteksAgent"]
    KA -->|"Embedding Sor"| C["Cohere V3"]
    C -->|"Vektör Sorgusu"| VDB[("Vektör DB")]
    VDB --"İlgili Parçalar"--> KA
    KA -->|"Wikipedia / Academic Search"| WEB["🌐 Web RAG"]
    KA -->|"Video Transkript Analizi"| YT["📺 YouTube Transcript Plugin"]
    WEB & YT --"Derlenmiş Veri"--> WA["WikiAgent"]
    WA -->|"Öğrenci Dostu Dil"| G["Grader"]
```

---

## 💹 5. PUANLAMA (Evaluation) ALGORİTMA MANTIĞI
*EvaluatorAgent hangi kriterlere göre puan keser veya verir?*

```mermaid
graph TD
    ANS["Ajan Cevabı"] --> EA["EvaluatorAgent"]
    
    EA -->|"Kritik 1: Pedagoji"| M1["Seviye Uyumu & Analogiler"]
    EA -->|"Kritik 2: Doğruluk"| M2["Piston Çıktısı vs Bilgi"]
    EA -->|"Kritik 3: Niyet"| M3["Kullanıcı Duygusu (???)"]
    
    M1 & M2 & M3 -->|"Ağırlıklı Ortalama"| SC["Sonuç Puan (1-10)"]
    SC --"Puan < 6"--> RED_ALERT["Hatalar Defterine Yaz (Kritik)"]
    SC --"Puan > 8"--> GOLD_DATA["Altın Örnek Olarak İşaretle"]
```

---

## 🏭 6. ORKA SANDBOX & OTONOM EVRİM (OALL)
*Sistemin kendi kendine zorlaşan senaryolarla gelişmesi.*

```mermaid
graph TD
    R[("Redis: Puanlar")] -->|"Düşük Puanlı Senaryolar"| SB["Sandbox Engine"]
    SB -->|"Persona Üretimi (A, B, C)"| U["Sentetik Öğrenci"]
    U -->|"Supervisor'ı Terlet"| SA["Supervisor"]
    SA -->|"Yanlış Yaparsa"| L405["Llama-405B (Hakem)"]
    L405 --"Sert Eleştiri"--> SB
    SB --"Daha Zor Senaryo (A+)"--> U
```

---

> [!NOTE]
> Bu modüler parçalar, en üstteki Global Mimari'nin "zoom yapılmış" teknik halleridir. Hiçbir kısıtlama kalmadı; Orka'nın tüm damarları burada.
