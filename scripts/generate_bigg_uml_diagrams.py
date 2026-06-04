from pathlib import Path
import math

from PIL import Image, ImageDraw, ImageFont


OUT_DIR = Path(r"D:\Orka\artifacts\bigg-cube\ekler")
OUT_DIR.mkdir(parents=True, exist_ok=True)

FONT_REG = r"C:\Windows\Fonts\arial.ttf"
FONT_BOLD = r"C:\Windows\Fonts\arialbd.ttf"


def font(size, bold=False):
    path = FONT_BOLD if bold else FONT_REG
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()


COLORS = {
    "ink": "#172033",
    "muted": "#5f6b7a",
    "line": "#9aa8b7",
    "bg": "#f6f8fb",
    "white": "#ffffff",
    "blue": "#1f6fbf",
    "blue_soft": "#e7f1fb",
    "teal": "#0f8b8d",
    "teal_soft": "#e5f4f4",
    "green": "#2e7d32",
    "green_soft": "#e9f5ea",
    "amber": "#b46b00",
    "amber_soft": "#fff4df",
    "purple": "#6c4bb8",
    "purple_soft": "#f0ecfb",
    "red": "#ad343e",
    "red_soft": "#fdecee",
    "dark": "#2d3748",
}


def wrap(draw, text, fnt, max_width):
    words = text.split()
    lines = []
    current = ""
    for word in words:
        probe = word if not current else f"{current} {word}"
        if draw.textbbox((0, 0), probe, font=fnt)[2] <= max_width:
            current = probe
        else:
            if current:
                lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


def rounded_box(draw, xy, title, body=None, fill="#ffffff", outline="#9aa8b7", accent=None, title_size=26, body_size=20):
    x1, y1, x2, y2 = xy
    draw.rounded_rectangle(xy, radius=22, fill=fill, outline=outline, width=3)
    if accent:
        draw.rounded_rectangle((x1, y1, x2, y1 + 16), radius=18, fill=accent, outline=accent)
    pad = 24
    title_font = font(title_size, True)
    body_font = font(body_size)
    draw.text((x1 + pad, y1 + 22), title, font=title_font, fill=COLORS["ink"])
    if body:
        y = y1 + 62
        for line in wrap(draw, body, body_font, x2 - x1 - pad * 2):
            draw.text((x1 + pad, y), line, font=body_font, fill=COLORS["muted"])
            y += body_size + 8


def arrow(draw, start, end, color="#516173", width=4, label=None, label_offset=(0, 0)):
    x1, y1 = start
    x2, y2 = end
    draw.line((x1, y1, x2, y2), fill=color, width=width)
    angle = math.atan2(y2 - y1, x2 - x1)
    size = 16
    pts = [
        (x2, y2),
        (x2 - size * math.cos(angle - math.pi / 6), y2 - size * math.sin(angle - math.pi / 6)),
        (x2 - size * math.cos(angle + math.pi / 6), y2 - size * math.sin(angle + math.pi / 6)),
    ]
    draw.polygon(pts, fill=color)
    if label:
        f = font(18, True)
        lx = (x1 + x2) / 2 + label_offset[0]
        ly = (y1 + y2) / 2 + label_offset[1]
        bbox = draw.textbbox((0, 0), label, font=f)
        draw.rounded_rectangle((lx - 10, ly - 6, lx + bbox[2] + 10, ly + bbox[3] + 6), radius=8, fill="#ffffff", outline="#d6dde6")
        draw.text((lx, ly), label, font=f, fill=color)


def elbow_arrow(draw, points, color="#516173", width=4, label=None, label_pos=None):
    for a, b in zip(points, points[1:]):
        draw.line((*a, *b), fill=color, width=width)
    x1, y1 = points[-2]
    x2, y2 = points[-1]
    angle = math.atan2(y2 - y1, x2 - x1)
    size = 16
    pts = [
        (x2, y2),
        (x2 - size * math.cos(angle - math.pi / 6), y2 - size * math.sin(angle - math.pi / 6)),
        (x2 - size * math.cos(angle + math.pi / 6), y2 - size * math.sin(angle + math.pi / 6)),
    ]
    draw.polygon(pts, fill=color)
    if label and label_pos:
        f = font(18, True)
        bbox = draw.textbbox((0, 0), label, font=f)
        lx, ly = label_pos
        draw.rounded_rectangle((lx - 10, ly - 6, lx + bbox[2] + 10, ly + bbox[3] + 6), radius=8, fill="#ffffff", outline="#d6dde6")
        draw.text((lx, ly), label, font=f, fill=color)


def header(draw, title, subtitle):
    draw.text((90, 55), title, font=font(42, True), fill=COLORS["ink"])
    draw.text((90, 110), subtitle, font=font(24), fill=COLORS["muted"])
    draw.line((90, 152, 2310, 152), fill="#d6dde6", width=3)


def save(img, name):
    path = OUT_DIR / name
    img.save(path)
    return path


def diagram_architecture():
    img = Image.new("RGB", (2400, 1600), COLORS["bg"])
    d = ImageDraw.Draw(img)
    header(d, "Ek-1 | OrkaOS Genel Sistem Mimarisi", "Kişisel yapay zeka öğrenme işletim sistemi - bileşen ve veri akışı UML görünümü")

    rounded_box(d, (90, 230, 520, 570), "Öğrenci Arayüzü", "Home / Mission Control, Tutor, Study Room, Review / Quiz, Exam War Room, Sources / Wiki, Notebook Studio, Code Learning IDE", COLORS["blue_soft"], "#9ec5ea", COLORS["blue"])
    rounded_box(d, (690, 230, 1110, 500), "API ve Güvenli Sunum", ".NET 8 API, auth, controller'lar, typed API kontratları, kullanıcıya güvenli DTO yüzeyi", COLORS["white"], "#9aa8b7", COLORS["dark"])
    rounded_box(d, (1280, 230, 1710, 500), "Öğrenme Orkestrasyonu", "Intent analizi, Agent Orchestrator, Tutor policy, plan diagnostic, modüller arası yönlendirme", COLORS["purple_soft"], "#b9a8e8", COLORS["purple"])
    rounded_box(d, (1880, 230, 2310, 500), "AI / Araç Katmanı", "LLM servisleri, embeddings, Semantic Kernel tool plane, Piston code runtime, provider fallback", COLORS["amber_soft"], "#edc77c", COLORS["amber"])

    rounded_box(d, (690, 650, 1110, 960), "Tutor ve Pedagoji", "Açıklama, telafi, mikro kontrol, Socratic yönlendirme, güvenli cevap politikası", COLORS["teal_soft"], "#9dd7d8", COLORS["teal"])
    rounded_box(d, (1280, 650, 1710, 960), "Ölçme / Hafıza", "Quiz attempt, mastery, learning signal, review / SRS, misconception ve telafi önerileri", COLORS["green_soft"], "#a7d3aa", COLORS["green"])
    rounded_box(d, (1880, 650, 2310, 960), "Kaynak / RAG / Wiki", "Source upload, retrieval, citation guard, Wiki evidence, Notebook paketleri", COLORS["blue_soft"], "#9ec5ea", COLORS["blue"])

    rounded_box(d, (90, 1110, 720, 1440), "Birleşik Öğrenme Durumu", "Mission Control tüm kanıtları birleştirir: konu, kaynak, quiz, tekrar, kod denemesi, sınav pratiği ve çalışma hafızası.", COLORS["white"], "#9aa8b7", COLORS["dark"])
    rounded_box(d, (860, 1110, 1480, 1440), "Veri Katmanı", "SQL Server: kullanıcı, oturum, konu, mesaj, kaynak, quiz, mastery, tool trace. Redis: kısa süreli bellek, cache, session state.", COLORS["white"], "#9aa8b7", COLORS["dark"])
    rounded_box(d, (1620, 1110, 2310, 1440), "Güvenlik / Telemetri", "JWT, KVKK odaklı veri sınırları, raw prompt/provider payload sızdırmama, maliyet ve runtime telemetry, log privacy guard.", COLORS["red_soft"], "#e9a0a6", COLORS["red"])

    arrow(d, (520, 400), (690, 365), label="istek")
    arrow(d, (1110, 365), (1280, 365), label="bağlam")
    arrow(d, (1710, 365), (1880, 365), label="kontrollü AI/tool")
    elbow_arrow(d, [(1495, 500), (1495, 585), (930, 585), (930, 650)], label="pedagojik karar", label_pos=(1040, 560))
    arrow(d, (1495, 500), (1495, 650), label="ölçme")
    elbow_arrow(d, [(1710, 490), (1760, 585), (2010, 585), (2010, 650)], label="kaynak", label_pos=(1840, 560))

    # Evidence writes are routed through a bus so arrows do not cross box text.
    bus_y = 1040
    d.line((260, bus_y, 2070, bus_y), fill="#7b8794", width=4)
    arrow(d, (900, 960), (900, bus_y), color="#7b8794")
    arrow(d, (1495, 960), (1495, bus_y), color="#7b8794")
    arrow(d, (2010, 960), (2010, bus_y), color="#7b8794")
    elbow_arrow(d, [(900, bus_y), (900, 1075), (560, 1075), (560, 1110)], color="#7b8794")
    elbow_arrow(d, [(1495, bus_y), (1495, 1075), (1180, 1075), (1180, 1110)], color="#7b8794")
    elbow_arrow(d, [(2010, bus_y), (2010, 1075), (1980, 1075), (1980, 1110)], color="#7b8794")
    arrow(d, (720, 1260), (860, 1260), label="persist")
    arrow(d, (1480, 1260), (1620, 1260), label="audit")
    elbow_arrow(d, [(350, 1110), (350, 1010), (280, 1010), (280, 570)], label="sonraki en iyi aksiyon", label_pos=(350, 920))

    return save(img, "Ek-1-OrkaOS-Genel-Sistem-Mimarisi-UML.png")


def diagram_learning_loop():
    img = Image.new("RGB", (2400, 1600), COLORS["bg"])
    d = ImageDraw.Draw(img)
    header(d, "Ek-2 | Orka Learning OS Adaptif Öğrenme Döngüsü", "Tanıla, planla, çalıştır, ölç, kanıt yaz, uyarlat")

    boxes = [
        ((90, 250, 470, 430), "1. Hedef / Konu", "Öğrenci konu, sınav hedefi veya çalışma niyeti girer.", COLORS["blue_soft"], COLORS["blue"]),
        ((600, 250, 980, 430), "2. Niyet Analizi", "Konu, odak alanı, ön bilgi ve kaynak ihtiyacı belirlenir.", COLORS["purple_soft"], COLORS["purple"]),
        ((1110, 250, 1490, 430), "3. Tanı / Quiz", "Diagnostik soru, mikro kontrol veya hızlı başlangıç çalışır.", COLORS["green_soft"], COLORS["green"]),
        ((1620, 250, 2310, 430), "4. Güvenli Değerlendirme", "Cevap sunucuda değerlendirilir; cevap anahtarı istemciye sızmaz.", COLORS["red_soft"], COLORS["red"]),
        ((1620, 620, 2310, 820), "5. Öğrenme Profili", "Zayıf kavram, mastery, review ihtiyacı, kaynak durumu ve güven bandı güncellenir.", COLORS["white"], COLORS["dark"]),
        ((1110, 620, 1490, 820), "6. Mission Control", "Birleşik durumdan bugünkü en iyi çalışma aksiyonu hesaplanır.", COLORS["blue_soft"], COLORS["blue"]),
        ((600, 620, 980, 820), "7. Mod Seçimi", "Tutor, Study Room, Review, Exam, Source/Wiki, Notebook veya Code IDE.", COLORS["amber_soft"], COLORS["amber"]),
        ((90, 620, 470, 820), "8. Çalışma Kanıtı", "Quiz sonucu, tutor trace, kaynak notu, code attempt veya review tamamlanması oluşur.", COLORS["teal_soft"], COLORS["teal"]),
    ]
    for xy, title, body, fill, accent in boxes:
        rounded_box(d, xy, title, body, fill, "#9aa8b7", accent, 25, 19)

    arrow(d, (470, 340), (600, 340))
    arrow(d, (980, 340), (1110, 340))
    arrow(d, (1490, 340), (1620, 340))
    arrow(d, (1970, 430), (1970, 620), label="kanıt")
    arrow(d, (1620, 710), (1490, 710))
    arrow(d, (1110, 710), (980, 710))
    arrow(d, (600, 710), (470, 710))
    arrow(d, (280, 620), (280, 430), label="geri besleme", label_offset=(20, -10))

    rounded_box(d, (360, 1020, 740, 1280), "Kanıt azsa", "Kısa diagnostik veya hızlı başlangıç önerilir.", COLORS["white"], "#d6dde6", "#9aa8b7", 24, 19)
    rounded_box(d, (820, 1020, 1200, 1280), "Tekrarlı hata varsa", "Tutor / Study Room telafi akışı önerilir.", COLORS["white"], "#d6dde6", "#9aa8b7", 24, 19)
    rounded_box(d, (1280, 1020, 1660, 1280), "Review zamanıysa", "Review / Flashcard / Quiz aksiyonu önerilir.", COLORS["white"], "#d6dde6", "#9aa8b7", 24, 19)
    rounded_box(d, (1740, 1020, 2120, 1280), "Kaynak veya kod varsa", "Sources / Wiki Pro, Notebook veya Code IDE aksiyonu önerilir.", COLORS["white"], "#d6dde6", "#9aa8b7", 24, 19)
    arrow(d, (1300, 820), (550, 1020), color="#7b8794")
    arrow(d, (1300, 820), (1010, 1020), color="#7b8794")
    arrow(d, (1300, 820), (1470, 1020), color="#7b8794")
    arrow(d, (1300, 820), (1930, 1020), color="#7b8794")

    d.text((90, 1440), "Kapalı döngü değer önerisi: ölç -> öğren -> kanıt yaz -> uyarlat -> sonraki en iyi aksiyon", font=font(30, True), fill=COLORS["ink"])
    return save(img, "Ek-2-OrkaOS-Adaptif-Ogrenme-Dongusu-UML.png")


def diagram_comparison():
    img = Image.new("RGB", (2400, 1600), COLORS["bg"])
    d = ImageDraw.Draw(img)
    header(d, "Ek-3 | Geleneksel Öğrenme Süreci vs OrkaOS", "Yenilikçi yön: dağınık araçlardan kanıta dayalı kişisel öğrenme işletim sistemine")

    d.rounded_rectangle((90, 230, 1120, 1430), radius=24, fill="#ffffff", outline="#d6dde6", width=3)
    d.rounded_rectangle((1280, 230, 2310, 1430), radius=24, fill="#ffffff", outline="#d6dde6", width=3)
    d.text((140, 275), "Geleneksel / Parçalı Süreç", font=font(34, True), fill=COLORS["red"])
    d.text((1330, 275), "OrkaOS Destekli Süreç", font=font(34, True), fill=COLORS["green"])

    left_items = [
        ("Sohbet botu", "Tekil cevap, kalıcı öğrenme durumu yok"),
        ("Video / içerik platformu", "İzleme var, kişisel aksiyon önerisi sınırlı"),
        ("Not ve kaynak araçları", "Kaynaklar ayrı, tekrar ve quiz ayrı"),
        ("Test bankası / LMS", "Sonuç var, modüller arası öğrenme hafızası zayıf"),
        ("Öğrenci manuel yönetir", "Ne çalışacağım, neden, hangi sırayla soruları öğrenci çözer"),
    ]
    y = 380
    for title, body in left_items:
        rounded_box(d, (150, y, 1020, y + 150), title, body, COLORS["red_soft"], "#e9a0a6", COLORS["red"], 25, 20)
        y += 190
    left_result = "Sonuç: cevap çok, fakat sürdürülebilir plan, tekrar disiplini ve kanıt temelli yönlendirme zayıf."
    y_text = 1320
    for line in wrap(d, left_result, font(24, True), 850):
        d.text((150, y_text), line, font=font(24, True), fill=COLORS["red"])
        y_text += 32

    right_items = [
        ("Birleşik öğrenme durumu", "Hedef, kaynak, hata, quiz, review, kod ve sınav pratiği aynı bağlamda"),
        ("Mission Control", "Bugünkü en iyi çalışma aksiyonu ve nedeni görünür"),
        ("Modüler çalışma alanları", "Tutor, Review/Quiz, Source/Wiki, Notebook, Exam, Code IDE"),
        ("Güvenli AI ve kanıt yönetimi", "Backend kontrollü AI/tool, kaynak dürüstlüğü, privacy-safe telemetry"),
        ("Kapalı döngü adaptasyon", "Her çalışma yeni kanıt yazar; sistem bir sonraki aksiyonu günceller"),
    ]
    y = 380
    for title, body in right_items:
        rounded_box(d, (1340, y, 2210, y + 150), title, body, COLORS["green_soft"], "#a7d3aa", COLORS["green"], 25, 20)
        y += 190
    right_result = "Sonuç: öğrenci yalnızca cevap almaz; doğru çalışma moduna yönlendirilir ve öğrenme döngüsü izlenir."
    y_text = 1320
    for line in wrap(d, right_result, font(24, True), 850):
        d.text((1340, y_text), line, font=font(24, True), fill=COLORS["green"])
        y_text += 32

    arrow(d, (1120, 820), (1280, 820), color=COLORS["blue"], width=7, label="yenilikçi dönüşüm", label_offset=(-70, -70))
    return save(img, "Ek-3-Geleneksel-Surec-vs-OrkaOS-UML.png")


def main():
    paths = [diagram_architecture(), diagram_learning_loop(), diagram_comparison()]
    for path in paths:
        print(path)


if __name__ == "__main__":
    main()
