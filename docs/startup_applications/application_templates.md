# Orka AI: Bulut & Startup Programları Başvuru Şablonları

Bu doküman, Microsoft Founders Hub, AWS Activate ve Google for Startups gibi programların başvuru formlarında (veya yatırımcı görüşmelerinde) kullanabileceğin **kopyala-yapıştır** hazır metinleri içerir. Bu metinler "Geliştirici" dilinden ziyade, değerlendirme komitelerinin (Mühendis + İş Geliştirmeciler) okumayı sevdiği **"Ürün, Vizyon ve Teknoloji (Business Value)"** dilinde yazılmıştır.

---

## 1. Genel Kurallar ve "Trikler"
- Başvuruları kesinlikle `@gmail.com` veya `@hotmail.com` ile yapma. Mutlaka bir domain alıp (örn: `ahmet@orka.ai` veya `hello@orka-ai.com`) **iş e-postası** ile başvur.
- LinkedIn profilini %100 doldur, "Founder @ Orka AI" unvanını ekle. Microsoft direkt olarak LinkedIn üzerinden doğrulama yapar.
- Şirketin (LTD/AŞ) henüz kurulu olmasına gerek yok. Çoğu program "Unfunded / Bootstrap" aşamasında tüzel kişilik aramaz, şahıs olarak başvurabilirsin.
- Geliştirme aşamasını sorarlarsa: **"Building MVP"** veya **"Prototyping"** seçmelisin.

---

## 2. Ortak Başvuru Soruları (Kopyala/Yapıştır)

### Q1: Startup'ınızı bir veya iki cümleyle açıklayın (Elevator Pitch)
**Türkçe:** 
"Orka AI, doğal dildeki herhangi bir konuyu analiz edip, hiyerarşik bir müfredat çıkaran ve o müfredatı çoklu-ajan (multi-agent) teknolojisiyle öğrenciye anlatan, ölçen ve kişiselleştiren otonom bir yapay zeka öğrenme platformudur."

**İngilizce:** 
"Orka AI is an autonomous, multi-agent learning platform that takes any natural language topic, generates a hierarchical curriculum, and acts as a personalized tutor, evaluator, and researcher to guide students through a tailored educational journey."

### Q2: Çözdüğünüz Problem Nedir? (The Problem)
**İngilizce:**
"Traditional online courses and standard LLM chat interfaces offer a one-size-fits-all, static learning experience. Students easily lose context, lack structured curriculums for complex topics, and struggle to evaluate their coding or theoretical knowledge without human intervention. Standard AI tools answer questions but do not track a student's weaknesses or build long-term memory for pedagogical adaptation."

### Q3: Sizin Çözümünüz Nedir? (The Solution)
**İngilizce:**
"Orka AI introduces a Closed-Loop Swarm Intelligence architecture. Instead of a single chatbot, we deploy a swarm of specialized AI agents (Tutor, Evaluator, DeepPlan, Korteks). 
1. **DeepPlan Agent** assesses the user via a baseline quiz and builds a custom module-based curriculum.
2. **Tutor Agent** teaches the material dynamically.
3. **Evaluator Agent** scores both the AI's pedagogy and the student's coding/quiz performance using multi-dimensional RAG triads. 
Everything is connected via a Redis-backed 'Long-Term Memory' that adapts the teaching style to the student's evolving weaknesses."

---

## 3. Platformlara Özel Teknoloji (Tech Stack) Açıklamaları

*Not: Değerlendiriciler, kendi platformlarını neden kullandığını duymak ister.*

### A. Microsoft Founders Hub (Microsoft'a Özel Metin)
**Q: Neden Azure ve Microsoft teknolojilerini tercih ediyorsunuz? / Azure'u nasıl kullanacaksınız?**
**İngilizce:**
"Orka AI is natively built on the Microsoft ecosystem, designed to maximize the capabilities of **.NET 8 Core** and **Microsoft Semantic Kernel**. Our multi-agent orchestrator relies heavily on Semantic Kernel plugins for LLM routing and intention classification. We are applying to Founders Hub to migrate our local SQL Server to **Azure SQL**, our local cache to **Azure Cache for Redis**, and most importantly, to utilize **Azure OpenAI Service**. Since our platform requires strict, zero-latency orchestration between 10+ AI agents, the enterprise-grade reliability and low latency of Azure OpenAI (specifically GPT-4o) are mission-critical for our real-time interactive IDE and TTS (Text-to-Speech) classroom features."

### B. AWS Activate (Amazon'a Özel Metin)
**Q: Mimariniz AWS üzerinde nasıl çalışacak?**
**İngilizce:**
"Orka AI operates on a highly decoupled, microservices-ready architecture perfect for AWS. We plan to use **Amazon ECS (Fargate)** to host our .NET 8 agent orchestrator securely and scalably. Our high-throughput agent evaluation logs and rate-limiting systems will run on **Amazon ElastiCache for Redis**. Furthermore, we aim to leverage **Amazon Bedrock** to access foundation models like Anthropic Claude 3 for our research and summarization agents, ensuring we use the best model for the specific task while staying within a unified cloud ecosystem."

### C. Google for Startups (Google'a Özel Metin)
**Q: Google Cloud platformu hedeflerinize nasıl yardımcı olacak?**
**İngilizce:**
"As an AI-First startup, we are deeply interested in the Gemini ecosystem. We plan to utilize **Google Cloud Run** to deploy our stateless multi-agent orchestrator containers, allowing us to scale from zero to thousands of concurrent students instantly. We intend to integrate **Vertex AI (Gemini Pro/Flash)** for our 'Korteks Deep Research' agent, taking advantage of its massive context window to synthesize large educational datasets and web scrapes. **Google Memorystore** will serve as the backbone for our agentic state machine and student profile caching."

---

## 4. Uygulamanın İş Modeli (Business Model)
*Eğer nasıl para kazanacağınızı sorarlarsa (How will you make money?):*
**İngilizce:**
"We operate on a B2C freemium SaaS model, charging a monthly subscription for unlimited AI-tutor sessions, interactive coding environments (sandbox), and advanced deep-research generations. Additionally, we plan a B2B channel where schools and bootcamps can license our 'Evaluator' and 'DeepPlan' APIs to create adaptive curriculums for their own student bases."

---

## Sonraki Adım
Bu metinleri kopyalayarak formlara yapıştırabilirsin. En büyük silahın **Microsoft Founders Hub** başvurusundaki `.NET 8` ve `Semantic Kernel` anahtar kelimeleridir. Başvuruyu gönderdikten sonra genellikle 3 ile 7 iş günü içerisinde onay (veya ret) e-postası gelir. Ret gelirse bile pes etmek yok, eksikleri tamamlayıp 30 gün sonra tekrar başvuru hakkın var!
