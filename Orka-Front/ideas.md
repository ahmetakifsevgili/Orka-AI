# Orka AI — Tasarım Beyin Fırtınası

## Referanslar
Linear.app, Perplexity.ai, Raycast.com, Vercel.com, Stripe.com

---

<response>
<text>

## Fikir 1: "Mühendislik Brutalizmı" (Engineering Brutalism)

**Tasarım Akımı:** Neo-Brutalist Minimalizm — Swiss Design ile dijital brutalizmin kesişimi. Tüm dekoratif unsurlar soyulmuş, yalnızca bilgi hiyerarşisi ve tipografi konuşuyor.

**Temel İlkeler:**
1. Radikal sadelik — her piksel bir amaca hizmet eder
2. Tipografi hiyerarşisi ile derinlik yaratma (renk yerine ağırlık/boyut)
3. Keskin kenarlar, sıfır border-radius, grid-tabanlı düzen

**Renk Felsefesi:** Tamamen monokrom. zinc-950 ile zinc-100 arasında 6 kademe. Renk yalnızca semantik amaçla (hata: kırmızı, başarı: yeşil). Duygusal ton: "soğuk profesyonellik."

**Düzen Paradigması:** Katı 3-sütun grid. Sütunlar arasında sadece 1px çizgi. Boşluk yerine çizgilerle ayrım.

**İmza Elementleri:**
- Monospace tipografi vurguları (kod estetiği)
- 1px keskin çizgiler ile bölüm ayrımı

**Etkileşim Felsefesi:** Sıfır animasyon. Anlık geçişler. Hover'da sadece renk değişimi.

**Animasyon:** Yok. Tüm geçişler anlık. "Hız = güç" hissi.

**Tipografi:** JetBrains Mono (başlıklar) + Inter (gövde). Ağırlık kontrastı: 700 vs 400.

</text>
<probability>0.05</probability>
</response>

---

<response>
<text>

## Fikir 2: "Sessiz Lüks" (Quiet Luxury) — Linear/Perplexity Sentezi

**Tasarım Akımı:** Ultra-Minimalist Dark Mode — Linear.app'in mühendislik disiplini ile Perplexity'nin fonksiyonel temizliğinin birleşimi. "Az ama mükemmel" felsefesi.

**Temel İlkeler:**
1. İçerik kraldır — UI asla içeriğin önüne geçmez
2. Negatif alan (whitespace) aktif bir tasarım elemanıdır
3. Mikro-etkileşimler fonksiyoneldir, dekoratif değil
4. Derinlik, renk yerine opaklık ve ince gölgelerle sağlanır

**Renk Felsefesi:** Zinc paleti üzerinde katmanlı derinlik sistemi. zinc-950 (en derin katman/sidebar) → zinc-900 (orta katman/canvas) → zinc-800 (yüzey elemanları). Beyaz metin opaklık kademeleriyle: %100, %60, %40. Renk yalnızca quiz feedback'inde (yeşil/kırmızı, düşük doygunlukta). Duygusal ton: "güvenilir zeka."

**Düzen Paradigması:** Asimetrik 3-panel düzen. Sol sidebar sabit (w-64), merkez esnek (flex-1), sağ wiki çekmecesi (w-80, koşullu). Paneller arasında 1px zinc-800 border. İçerik max-w-3xl ile merkezlenmiş.

**İmza Elementleri:**
- Çok ince border'lar (zinc-800) ile katman ayrımı — gölge yerine çizgi
- Input alanının "komuta merkezi" hissi — Raycast'ten ilham

**Etkileşim Felsefesi:** Her etkileşim 150-200ms içinde tamamlanır. Hover'da sadece renk geçişi (text-zinc-500 → text-zinc-300). Tıklama geri bildirimi anlık. Wiki drawer 250ms slide-in.

**Animasyon:**
- Mesaj girişi: opacity 0→1, y: 8→0, 250ms ease-out
- Wiki drawer: translateX 320→0, 250ms ease-out
- Thinking indicator: 3 nokta, opacity pulse, 1.2s döngü
- Sayfa geçişleri: opacity fade, 150ms

**Tipografi:** Inter (tüm ağırlıklar: 400, 500, 600). Başlıklar: text-lg font-semibold. Gövde: text-sm leading-relaxed. Etiketler: text-xs font-medium uppercase tracking-wider text-zinc-500. AI yanıtları: prose prose-invert prose-sm.

</text>
<probability>0.08</probability>
</response>

---

<response>
<text>

## Fikir 3: "Kozmik Derinlik" (Cosmic Depth)

**Tasarım Akımı:** Dark Atmospheric — Vercel'in derinlik hissi ile Stripe'ın premium dokusunun birleşimi. Karanlık uzay metaforu.

**Temel İlkeler:**
1. Katmanlı derinlik — her panel farklı bir "yükseklikte"
2. Işık kaynağı metaforu — üstten gelen ince ışık efekti
3. Organik geçişler ve yumuşak kenarlar

**Renk Felsefesi:** zinc-950 taban üzerine çok ince radial gradient'ler (zinc-900 → zinc-950). Paneller arasında subtle glow border. Duygusal ton: "kozmik keşif."

**Düzen Paradigması:** Floating panel sistemi. Paneller hafif gölgelerle "havada" duruyor hissi.

**İmza Elementleri:**
- Çok ince gradient border'lar (zinc-700 → transparent)
- Radial gradient arka planlar

**Etkileşim Felsefesi:** Yumuşak spring animasyonlar. Hover'da scale(1.01) ve gölge artışı.

**Animasyon:** Spring physics, 300-400ms geçişler. Parallax scroll efektleri landing page'de.

**Tipografi:** Geist Sans (başlıklar) + Inter (gövde). Letter-spacing: -0.02em başlıklarda.

</text>
<probability>0.04</probability>
</response>

---

## Seçim: Fikir 2 — "Sessiz Lüks" (Quiet Luxury)

Bu yaklaşım, kullanıcının istediği Linear.app + Perplexity.ai referanslarına en sadık kalırken, proje gereksinimlerindeki "kesinlikle neon renk yok, glassmorphism yok, bouncy animasyon yok" kurallarına tam uyum sağlıyor. Zinc paleti üzerinde katmanlı derinlik sistemi, fonksiyonel mikro-etkileşimler ve tipografi-odaklı hiyerarşi ile "mühendislik kalitesini görselliğiyle hissettiren" bir deneyim sunacak.
