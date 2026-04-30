---
description: Orka Frontend (React 19, Vite, Tailwind v4) kuralları. Tasarım sistemi, component sınırları ve state yönetimi.
globs:
  - "Orka-Front/src/**/*.tsx"
  - "Orka-Front/src/**/*.ts"
  - "Orka-Front/src/**/*.css"
alwaysApply: false
---

# Frontend Kuralları — React 19 / Vite 6 / Tailwind v4

## 1. Mimari Sınırlar (Zorunlu)
- **State Yönetimi:** Redux, Zustand, MobX KULLANILMAZ. Sadece `useState`, `useContext`, `useRef`. Global state `Home.tsx` üzerinde toplanır ve aşağı prop olarak geçer.
- **Routing:** Sadece `wouter` kullanılır (`react-router-dom` yasak).
- **API İstekleri:** Yalnızca `services/api.ts` üzerindeki tek Axios instance üzerinden yapılır. Streaming SSE istekleri `fetch` ile manuel parse edilir.
- **Hook Kuralları:** Tüm Hook'lar koşulsuz en üstte çağrılır. `useCallback` performansı için zorunludur.

## 2. 2026 UI/UX Tasarım Sistemi (Aksine İzin Verilemez)
Kullanıcı "winrar gibi olmasın, modern, ferah ve sade olsun" kuralını koymuştur. Şunlar KESİNLİKLE YASAKTIR:
- **Yasaklı Renkler:** `red-*`, `blue-*`, `purple-*`, `pink-*`, `gradient-*` SIFIR tolerans.
- **Yasaklı Efektler:** Glassmorphism (`backdrop-blur`), neon gölgeler (`glow`), heavy shadows.
- **Yasaklı Component:** Chat mesajı içinde Bubble (balon) stili, eski daktilo (typewriter) efekti (Sadece `▊` streaming cursor kullanılacak).

**İzin Verilen Görsel Yapı:**
- Sadece `Geist` fontu.
- Sadece üç renk ailesi: `zinc-*` (arkaplan/metin), `emerald-*` (başarı/fokus), `amber-*` (uyarı/quiz/hata).
- Sadece ince 1px hairline border'lar (`border-zinc-800`). Flat ve minimal tasarım.

## 3. Component Yerleşimi ve Artifact Panel Pattern
- IDE, Quiz ve Wiki ekranları sohbet içine GÖMÜLMEZ. Bunlar sağ taraftan kayarak açılan **Artifact Panel** içinde gösterilir.
- Mesaj satır içi değil, düz döküman (prose-invert) formatında render edilir.

## 4. SSE (Server-Sent Events) Parse Mekanizması
- `ChatPanel.tsx` SSE okurken her satırı `data: ` öneki ile kontrol eder.
- `[THINKING: ...]` mesajları asla UI'a basılmaz, sadece statüsü günceller.
- `[TOPIC_COMPLETE:id]` ve `[PLAN_READY]` UI'da kart/toast olarak gösterilir.
