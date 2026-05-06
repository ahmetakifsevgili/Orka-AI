import { useEffect } from "react";
import { driver } from "driver.js";
import "driver.js/dist/driver.css";
import "./PremiumOnboardingTour.css";
import { storage } from "@/services/api";

export function usePremiumOnboarding() {
  useEffect(() => {
    const user = storage.getUser();
    if (!user) return;

    const storageKey = `orka_premium_tour_seen_v3_${user.id}`;

    const tourSeen = localStorage.getItem(storageKey);
    if (tourSeen === "true") return;

    const timer = setTimeout(() => {
      if (!document.getElementById("tour-new-topic")) return;

      const driverObj = driver({
        showProgress: true,
        animate: true,
        allowClose: true,
        doneBtnText: "Tamam",
        nextBtnText: "İleri",
        prevBtnText: "Geri",
        progressText: "{{current}} / {{total}}",
        popoverClass: "orka-premium-tour",
        onDestroyStarted: () => {
          localStorage.setItem(storageKey, "true");
          driverObj.destroy();
        },
        steps: [
          {
            element: "#tour-new-topic",
            popover: {
              title: "Küçük bir hedefle başla",
              description:
                "Orka önce bugün ne çalışmak istediğini netleştirir. Konu, kaynak, kod hatası veya tekrar döngüsüyle başlayabilirsin.",
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-dashboard",
            popover: {
              title: "Bugünkü çalışma odağı",
              description:
                "Burası sana sıradaki küçük adımı gösterir. Gerçek ilerleme sinyalleri çözdükçe, kod yazdıkça, kaynakla çalıştıkça ve tekrar yaptıkça oluşur.",
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-learning",
            popover: {
              title: "Tekrar döngüsü",
              description:
                "Flashcard, tekrar, günlük mini görev ve bookmark parçaları ayrı kutular değil; Orka'nın öğrendiklerini yeniden çalışmaya çevirdiği döngüdür.",
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-wiki",
            popover: {
              title: "Kaynak ve wiki hafızası",
              description:
                "Kendi dokümanların, wiki notları ve kaynaklı cevaplar burada anlam kazanır. Orka cevap verirken dayanak varsa bunu ayrıca gösterir.",
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-ide",
            popover: {
              title: "Kod hatası da ders malzemesi",
              description:
                "IDE çıktısı Tutor'a öğretici bağlam olarak akar. Compile, runtime veya timeout hataları uygulama arızası değil, çalışılacak bir sonraki ipucudur.",
              side: "right",
              align: "start",
            },
          },
        ],
      });

      driverObj.drive();
    }, 1500);

    return () => clearTimeout(timer);
  }, []);
}
