# Orka Frontend Redesign — Nisan 2026 Referans Planı

> Bu dosya **bağlayıcı kural değil**, kapsamlı redesign için referanstır.
> Hedef: Claude/Cursor/Linear/v0 tarzı sade, modern, klavye-dostu 2026
> ajan arayüzü. `frontend.md` bağlayıcı kurallarla birlikte okunur —
> çakışma varsa `frontend.md` kazanır, gerekirse bu plan yürütülürken
> kurallar güncellenir.

---

## 1) Neyi Değiştirmeye Çalışıyoruz

Kullanıcının sözü: *"yeni nesil sade anlaşılır göz yormayan ajan arayüzleri
gibi olsun, winrar gibi olmasın, 2026 Nisan'dayız."*

Anlamı: **daha az chrome, daha çok tipografi; daha az icon kalabalığı,
daha çok klavye akışı; daha az kutu, daha çok boşluk**.

---

## 2) Mevcut Durum Denetimi

**İyi temel:**
- React 19 + Vite 6 + Tailwind v4 — 2026'da uygun stack
- shadcn-style token'lar (`oklch`) `index.css`'de
- Zinc + emerald + amber palet kuralı yerleşik
- Wouter (hafif) + framer-motion + lucide-react
- Dark mode default, `prose-orka` tipografi ayarı var

**Rafine edilecek:**
- `.glass-panel` + `backdrop-filter: blur(24px) saturate(180%)` → retro efekt, kaldırılmalı
- `.glow-silver` / `.glow-silver-active` → 2022 neon estetiği
- Light-mode CSS override bloğu → kural dark-only, ölü kod
- `LeftSidebar` 4 nav item + dinamik topic listesi + yeni topic formu → görsel yük fazla
- `ChatPanel` composer: Send + Sparkles + BookOpen + Bell + Globe — 5 ikonlu toolbar, dağınık
- Font scale 3-kademe (`small/medium/large`) nice ama default 14px biraz küçük
- Quiz/Wiki/IDE hepsi "component panel" olarak sırayla açılıyor — modern ajanlarda **artifact panel** pattern'i tek alan
- Typewriter efekti karakter bazlı → streaming cursor'a geçilmeli
- `DashboardPanel` kartlarında padding yoğun ama hiyerarşi düşük

---

## 3) Referans Sistemler (Nisan 2026 Benchmark)

| Ürün | Alınacak Pattern |
|---|---|
| Claude (Anthropic) | Artifact panel (sağ), sade mesaj tipografisi, yok-kabuk |
| ChatGPT / o1 | Minimal composer, thinking reveal, mesaj bubble'sız |
| Perplexity | Inline citation `[1]`, research mode göstergesi |
| Cursor / v0 / bolt.new | IDE + chat hibrit, sağ panel editor |
| Linear | Sidebar rail + Cmd+K, klavye-first, breadcrumb |
| Raycast | Cmd+K komut palet altın standardı |
| Arc / Dia | İnce yan rail, hover-expand sidebar |
| Notion 2026 AI | Slash commands, block-level AI |
| Vercel Dashboard | Büyük tipografi, sıfır dekorasyon, hairline border |
| Phind / Morph | Dev-focused chat, kod blok-öncelikli |

---

## 4) Tasarım İlkeleri (7 Madde)

1. **Az şey, büyük boşluk.** Her panel nefes alsın; flex + gap-6+ varsayılan.
2. **Tipografi > kutu.** Mesaj bubble yok. Satır ve ağırlıkla ayır.
3. **Hareket telaşsız.** 150-200ms ease-out; bouncy yasak; layout shift yasak.
4. **Klavye her şeyin önünde.** Cmd+K palette, j/k nav, Esc close, `/` slash.
5. **Artifact > modal.** Quiz/IDE/Wiki sağdan açılan panel. Modal sadece destructive confirm.
6. **Durum göstergeleri küçük.** Thinking inline chip, status dot; dev etiket değil.
7. **Chrome yok.** Gradient yok, blur yok, heavy shadow yok, neon glow yok. Sadece hairline border (1px, %10-15 opacity).

---

## 5) Renk & Tipografi Yeniden

### Palet (minimum 3-katman)
```
bg-base     zinc-950    (arka)
bg-elevate  zinc-900    (panel / kart)
bg-overlay  zinc-800    (hover / active)
text-primary   zinc-100 (ana metin)
text-muted     zinc-400 (ikincil)
text-faint     zinc-500 (tarih, hint)

focus        emerald-400  (interactive)
focus-bg     emerald-500/10 + text-emerald-400  (primary action)
signal       amber-400    (uyarı, quiz, kritik)
```

Kaldırılacak CSS utility'leri:
- `.glass-panel` (tamamen sil)
- `.glow-silver`, `.glow-silver-active` (tamamen sil)
- `html.light` blok (dark-only kural — ölü kod)

### Tipografi
- **Sans:** Geist Sans (veya Inter variable, zaten mevcut)
- **Mono:** Geist Mono (veya JetBrains Mono)
- **Scale:** 12 / 14 / 16 / 20 / 28 — 5 katman, daha fazlası dağılır
- **Satır yüksekliği:** body 1.55, heading 1.2
- **Default font-size:** 15px (`--font-scale` ile ölçeklenebilir kalsın)

### Radius
```
sm: 6px   (chip, badge)
md: 10px  (button, input, small card)
lg: 14px  (panel, modal)
full:     (composer, pill)
```

### Shadow
- Default: **yok**
- Hover: `0 1px 2px rgba(0,0,0,0.4)`
- Elevated (modal/artifact): `0 8px 32px rgba(0,0,0,0.6)`

---

## 6) Layout Yeniden Düzeni

**Mevcut:** Sidebar (24%) + Chat (76%) + modal overlay'ler
**Önerilen:**

```
┌──────┬────────────┬──────────────────────────┬─────────────────┐
│ Rail │ Thread     │ Main                     │ Artifact Panel  │
│ 48px │ Pane       │ (flex, breadcrumb+thread │ (sağdan kayar,  │
│ icon │ 320px,     │  +composer)              │  resizable      │
│-only │ collapse   │                          │  480-720px)     │
└──────┴────────────┴──────────────────────────┴─────────────────┘
```

### (A) Navigation Rail (48px, solda)
- En üstte logo (`OrcaLogo`) 32px
- İkonlar (lucide 20px): `MessageSquare` (Chat), `BookMarked` (Wiki), `Code2` (IDE), `LayoutDashboard` (Dashboard, admin-only)
- En altta: Profile avatar + ayarlar dişlisi
- Active rail item: emerald-400 sol-border 2px + subtle bg
- Hover: label tooltip (radix tooltip)

### (B) Thread Pane (320px, toggle'lı)
- Üstte header: "Konular" + `Plus` button
- Search input (ince, border-b)
- Sonra aktif konular listesi (son 10)
- Her item: başlık + phase rozeti (amber-400/15) + son mesaj zaman damgası
- Footer: kısayol ipucu chip `⌘K`

### (C) Main
- Üstte breadcrumb: `Konu › Alt Konu › Oturum #4`
- İçerik: thread center-aligned, **max-w 720px**
- Composer bottom-sticky, **pill shape**, full width - 48

### (D) Artifact Panel (sağdan kayar, 480px default, resizable)
- Tab bar üstte: Wiki · IDE · Quiz · Notlar (aktif tab alt-border emerald-400)
- İçerik alanı
- Esc kapatır, Cmd+/ toggle
- Hangi AI aksiyonu açtıysa otomatik odaklanır

---

## 7) Bileşen-Bileşen Redesign Notları

### ChatMessage
**Mevcut:** AI/User bubble tarzı, farklı branch'ler (quiz/topic_complete/ide)

**Hedef:**
- User mesajı: sağa yaslı değil, **sol yaslı** + `border-l-2 border-amber-500/60 pl-3` + `text-zinc-300`
- AI mesajı: avatar yok, sadece üstte 12px `Orka` etiketi `text-zinc-500`, altında body `text-zinc-100`
- `topic_complete`: tek satır zinc-900 card — `✓ "Topic X" tamamlandı. Wiki'ye bak →` (tıklanabilir)
- `plan_ready`: üst toast (mesaj değil) + breadcrumb'da yeni Plan tab
- Quiz: **inline render kaldır** → Artifact Panel'e yönlendir, mesajda `→ Quiz aç` butonu
- Streaming: sonuna `▊` blinking cursor (typewriter kaldır)
- Kod blokları: `Geist Mono`, bg-zinc-950, border-zinc-800, kopyala butonu hover'da görünsün

### Composer (ChatPanel input bölümü)
**Mevcut:** Send + Sparkles + BookOpen + Bell + Globe ikonları
**Hedef:**
```
┌─────────────────────────────────────────────────────────────┐
│ /  Bir şey sor, Korteks'i dene, plan iste...           ↵  │
│                                                             │
│ [Chat] [Plan] [Korteks]                        0/4000      │
└─────────────────────────────────────────────────────────────┘
```
- Pill shape (`rounded-full`), border `zinc-800`, focus içi subtle emerald ring
- Sol: `/` ikonu (slash command trigger)
- Sağ: `Enter` ikonu (gönder)
- Alt satır: mod chip'leri (radio-style toggle) + token sayacı
- Bell/Globe → Rail'e veya Palette'e taşı. Composer sadece yazma + mod.

### Thinking Indicator
**Mevcut:** Block-level rotating text card
**Hedef:**
- Inline chip (zinc-800 bg, zinc-400 text, 12px) mesaj yerine:
  `●●● Düşünüyor...` → `●●● Plan hazırlıyor...` (rotation)
- `●` dot'lar `animate-pulse` ile
- Cursor `▊` body'nin en sonunda

### Quiz Card
- **Mesaj içinde değil** → Artifact Panel'de
- Soru büyük (20px), options dikey yaslı radio (checkbox değil)
- Kod sorusu → inline **mini** Monaco (full IDE değil, 240px yüksek)
- Submit full-width, emerald bg, disabled state zinc-800
- Cevap sonrası: Inline doğru/yanlış rozeti + kısa AI yorumu

### InteractiveIDE
- Full-screen overlay **değil** → Artifact Panel tab
- Sol %60 editor (Monaco), sağ %40 output
- Output: Piston response + doğruluk rozeti (emerald ✓ veya amber ✗)
- Language picker küçük dropdown (üstte)

### Wiki (WikiMainPanel + WikiDrawer birleşmeli)
- Artifact Panel tab'ı "Wiki"
- TOC sol 180px, content sağ
- Copilot: **bottom sheet** (48px collapsed, expand on click), Discord-style
- Loading skeleton: 3 satır pulsing bar (mevcut WikiGeneratingSkeleton iyileştirilsin)

### LeftSidebar → Rail + Thread Pane'e böl
- `LeftSidebar.tsx` dosyasını ikiye ayır: `NavigationRail.tsx` (48px) + `ThreadPane.tsx` (320px)
- Topic listesi ThreadPane'e taşınır
- Yeni topic formu modal yerine ThreadPane inline (tek satır input)

### SystemHealthHUD (LLMOps)
- **Mevcut:** Kart gibi görünüyor
- **Hedef:** Bottom-right status chip, `pt-2 pb-2 px-3 rounded-full bg-zinc-900 border border-zinc-800`
  - `● 87% Primary · 7.8 avg · 42ms` (tek satır, mono)
  - `●` renkli dot: emerald (sağlıklı), amber (degrade)
  - Click → bottom sheet modal ile detay

### Dashboard
- 3x2 grid KPI kartları:
  - Büyük rakam (32px, Geist Mono)
  - Alt: label (12px, zinc-500)
  - Mini sparkline (24px, stroke-zinc-400)
- Border yok, sadece `p-6 bg-zinc-900 rounded-lg`
- Tablolar: zebra stripes yok, hairline `border-b-zinc-800`

---

## 8) Yeni Yetenekler (2026 Standard)

### Cmd+K Palette
- `cmdk` library kur
- Actions:
  - Yeni Konu
  - Konu Ara
  - Profil / Ayarlar
  - Tema Değiştir (dark var; future multi-theme)
  - Çıkış Yap
  - Mod: Plan / Chat / Korteks
- Shortcut: Cmd+K (Ctrl+K Windows)

### Slash Commands (Composer)
- `/plan` → plan modu toggle
- `/wiki` → artifact panel wiki aç
- `/ide` → artifact panel ide aç
- `/quiz` → baseline quiz iste
- `/kortex` → research modu

### Keyboard Shortcuts (Linear benzeri)
| Kısayol | Aksiyon |
|---|---|
| `⌘K` | Palette |
| `⌘/` | Artifact panel toggle |
| `⌘⏎` | Force send |
| `⌘⇧N` | Yeni konu |
| `Esc` | Panel kapat |
| `J / K` | Thread nav |

### Inline Citations (Korteks)
- `[1]` `[2]` hover → kaynak preview (radix hovercard)
- Mesaj altında toplu kaynak listesi değil, inline

### Streaming Polish
- Karakter typewriter **YASAK**
- Tek ▊ cursor yanıt sonuna
- Token akışını React'te `useDeferredValue` ile batch'le (jank azaltır)

---

## 9) Uygulama Sırası (Fazlar)

### Faz 1 — Temel Temizlik (1-2 gün)
- `index.css`: `.glass-panel`, `.glow-*`, `html.light` blokları sil
- Komponentlerde `backdrop-blur`, `bg-gradient-*` kalıntılarını tara ve sil
- Font'u Geist'e geçir (opsiyonel: Inter-variable kalabilir)
- Default font-size 15px'e al
- Shadow token'larını yeni skalaya uyar

### Faz 2 — Layout İskeleti (2-3 gün)
- `LeftSidebar.tsx` → `NavigationRail.tsx` + `ThreadPane.tsx`
- `Home.tsx` grid'i yeniden: `48px | 320px | 1fr | 480px(artifact)`
- Breadcrumb component `Main`'in üstüne
- Thread center-align + max-w 720px

### Faz 3 — Artifact Panel (2-3 gün)
- `ArtifactPanel.tsx` yeni component
- Tab bar: Wiki / IDE / Quiz / Notlar
- Mevcut `WikiMainPanel`, `InteractiveIDE`, `QuizCard`'ı bu panel içine taşı
- Cmd+/ toggle + Esc close

### Faz 4 — Composer Yeniden (1-2 gün)
- Pill shape, slash trigger, mod chips
- İkon gridini temizle (Bell/Globe/Sparkles → kaldır)
- Token counter sağ alt

### Faz 5 — Mesaj Tipografisi (1-2 gün)
- Bubble kaldır, satır-tabanlı render
- Streaming cursor ▊ (typewriter kaldır)
- Inline thinking chip
- `topic_complete` / `plan_ready` küçük kart + toast

### Faz 6 — Cmd+K Palette (1 gün)
- `cmdk` kur, provider mount
- Actions listesi

### Faz 7 — HUD + Dashboard (1-2 gün)
- SystemHealthHUD bottom-right chip
- DashboardPanel grid yeniden (büyük rakam + sparkline)

### Faz 8 — Cilalama (1 gün)
- Keyboard shortcuts bind
- `prefers-reduced-motion` respect
- Accessibility geçişi: focus-visible ring, ARIA labels, kontrast audit

**Toplam:** ~12-18 gün (solo, yarım-tam gün)

---

## 10) Yeni Paket Bağımlılıkları

| Paket | Ne için |
|---|---|
| `cmdk` | Cmd+K palette |
| `sonner` | `react-hot-toast` yerine (daha minimal) |
| `react-resizable-panels` | Artifact panel resize |
| `geist` (veya `@next/font`) | Font (opsiyonel) |

Tutulacak: `framer-motion`, `lucide-react`, `wouter`, `react-markdown`, Monaco.

---

## 11) Yasaklar Listesi (Kurallara Eklenmeli)

`frontend.md`'ye eklenecek:
- `backdrop-filter: blur()` → tüm komponentler için yasak
- `.glass-panel`, `.glow-*` class'ları → var olmayacak
- Message bubble (rounded-2xl + bg-elevate) → yasak
- Karakter typewriter efekti → yasak; streaming cursor tek yol
- 3'ten fazla font ailesi → yasak
- `bg-gradient-*` → zaten yasak, tekrar vurgula
- 5'ten fazla typography size → yasak
- `animate-bounce` / `animate-spin` except loading spinner

---

## 12) Erişilebilirlik (A11y)

- `focus-visible:ring-2 ring-emerald-400/60` her interactive element
- Kontrast: tüm metin AA seviyesi (zinc-400 on zinc-950 = 5.3:1 ✓)
- `aria-label` her icon-only button
- `prefers-reduced-motion` — `motion.*` → `AnimatePresence` sadece opacity kullanır
- Tab order: Rail → ThreadPane → Main → ArtifactPanel
- Esc her modal/panel kapatır

---

## 13) Risk & Dikkat

- **Artifact panel** mevcut `SplitPane` + `WikiMainPanel` + `InteractiveIDE` birleşimi → state yönetimi tek yerden (Home.tsx → ArtifactContext)
- **Keyboard shortcut çakışması** — browser default'ları ezme (Cmd+T, Cmd+N tehlike)
- **Monaco yükleme** — artifact panel lazy-load olsun, mount'ta değil
- **Mobile** — bu plan masaüstü odaklı; mobilde rail + main tek kolona düşer, artifact full-screen modal
- **Faz 1 sonrası tsc + healthcheck** geçmeli; her faz bağımsız test edilebilir olmalı

---

## 14) Dışarıya Bakılacak Repo/Ürünler (araştırma linkleri)

- Claude.ai — artifact panel implementasyonu (DOM inspect)
- v0.dev — composer + generated preview pattern
- linear.app — rail + command palette + keyboard
- cursor.com — IDE + chat hibrit
- vercel.com/dashboard — minimalist KPI tipografi
- t3.chat — hızlı modern chat UX (2025-2026 referans)
- morph.so — dev odaklı chat
- raycast.com/extensions — command palette interaction

---

## 15) Özet — Bu Planı Çalıştırırken Hatırla

1. **Her faz sonunda `npx tsc --noEmit` + `node scripts/healthcheck.mjs`** — hiçbir faz regresyon getirmesin.
2. Mevcut **davranışları** koruyarak UI'yı değiştir — API katmanı / state akışı DOKUNULMAZ.
3. Kullanıcı test döngüsü: her faz bitince tarayıcıda manuel gez.
4. Bu dosya güncellenir — ilerleme oldukça madde madde "[x] Faz 1 tamam" işaretle.

Son güncelleme: 2026-04-18
