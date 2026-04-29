import { useEffect } from "react";
import { driver } from "driver.js";
import "driver.js/dist/driver.css";
import "./PremiumOnboardingTour.css"; // Custom overrides
import { storage } from "@/services/api";

export function usePremiumOnboarding() {
  useEffect(() => {
    const user = storage.getUser();
    if (!user) return;

    const storageKey = `orka_premium_tour_seen_v2_${user.id}`;

    // Sadece Dashboard / Home üzerindeyken ve daha önce izlenmemişse
    const tourSeen = localStorage.getItem(storageKey);
    if (tourSeen === "true") return;

    // Elementlerin DOM'a yerleşmesi için kısa bir gecikme
    const timer = setTimeout(() => {
      // Eğer ana elemanlardan biri yoksa (sayfa henüz render olmadıysa) başlama
      if (!document.getElementById("tour-new-topic")) return;

      const driverObj = driver({
        showProgress: true,
        animate: true,
        allowClose: false,
        doneBtnText: "Başla",
        nextBtnText: "İleri →",
        prevBtnText: "← Geri",
        progressText: "{{current}} / {{total}}",
        popoverClass: "orka-premium-tour",
        onDestroyStarted: () => {
          if (!driverObj.hasNextStep()) {
            localStorage.setItem(storageKey, "true");
            driverObj.destroy();
          } else {
             // İptal (skip) edilirse de kapatılsın ama belki de skip eden daha sonra görmek ister.
             // Biz şimdilik her şekilde kapatıldığında bitmiş sayıyoruz ki sürekli çıkmasın.
             localStorage.setItem(storageKey, "true");
             driverObj.destroy();
          }
        },
        steps: [
          {
            element: "#tour-new-topic",
            popover: {
              title: "Öğrenme Serüveninizi Başlatın 🚀",
              description: "Yeni bir konu başlatarak hedefinizi belirleyin. Orka sizin için dinamik ve kişiselleştirilmiş bir müfredat inşa edecek.",
              side: "right",
              align: "start"
            }
          },
          {
            element: "#tour-nav-dashboard",
            popover: {
              title: "Performans Analitiği 📊",
              description: "Gelişim ivmenizi buradan takip edin. Başarı oranınız ve öğrenme seriniz hedeflerinize giden yolda en güçlü motivasyonunuz olacak.",
              side: "right",
              align: "start"
            }
          },
          {
            element: "#tour-nav-wiki",
            popover: {
              title: "Dinamik Bilgi Hafızası 🧠",
              description: "Öğrendiğiniz her kavram, tamamen size özel ve sürekli güncellenen bir Wiki kütüphanesine dönüşür. Bilgi artık asla kaybolmaz.",
              side: "right",
              align: "start"
            }
          },
          {
            element: "#tour-nav-ide",
            popover: {
              title: "Entegre Kod Editörü 💻",
              description: "Öğrendiklerinizi anında pratiğe dökün. Güvenli sandbox ortamında kodunuzu yazın, çalıştırın ve yapay zeka ile hata ayıklayın.",
              side: "right",
              align: "start"
            }
          }
        ]
      });

      driverObj.drive();
    }, 1500); // 1.5 sn gecikme (yükleme animasyonları için)

    return () => clearTimeout(timer);
  }, []);
}
