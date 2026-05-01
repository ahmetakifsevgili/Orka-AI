import { useCallback, useEffect, useMemo, useState } from "react";
import { driver, type DriveStep } from "driver.js";
import "driver.js/dist/driver.css";
import "./PremiumOnboardingTour.css";
import { storage } from "@/services/api";

const ONBOARDING_VERSION = "v3";
const TOUR_START_DELAY_MS = 140;
const WELCOME_DELAY_MS = 700;

type TourTarget = {
  selector: string;
  fallbackSelector?: string;
  title: string;
  description: string;
  side?: "top" | "right" | "bottom" | "left" | "over";
  align?: "start" | "center" | "end";
};

const TOUR_TARGETS: TourTarget[] = [
  {
    selector: "#tour-chat-input",
    fallbackSelector: "#tour-new-topic",
    title: "İlk hedefini buraya yaz",
    description: "Konu, sınav, kaynak ya da kod hedefini tek cümleyle yaz. Orka bunu plan, sohbet ve kişisel öğrenme akışına çevirir.",
    side: "top",
    align: "center",
  },
  {
    selector: "#tour-nav-dashboard",
    title: "Kokpit gelişimini toplar",
    description: "Doğruluk, seri, zayıf beceriler ve sıradaki en iyi adım tek sakin merkezde görünür.",
    side: "right",
    align: "start",
  },
  {
    selector: "#tour-nav-wiki",
    title: "Wiki kişisel hafızandır",
    description: "Ders notları, NotebookLM kaynakları, kartlar ve pekiştirme önerileri burada şekillenir.",
    side: "right",
    align: "start",
  },
  {
    selector: "#tour-nav-ide",
    title: "IDE hocaya bağlı çalışır",
    description: "Kodunu çalıştır, çıktıyı hocaya gönder; hata ve başarı sinyalleri öğrenme hafızana yazılır.",
    side: "right",
    align: "start",
  },
  {
    selector: "#tour-learning-signals",
    fallbackSelector: "#tour-global-stats",
    title: "Sinyal defteri seni kişiselleştirir",
    description: "Quiz cevapları, anlamadım sinyalleri, Wiki tıklamaları ve IDE çıktıları telafi planına dönüşür.",
    side: "top",
    align: "center",
  },
];

function getStorageKeys(userId: string) {
  return {
    current: `orka_onboarding_seen_${ONBOARDING_VERSION}_${userId}`,
    legacy: `orka_premium_tour_seen_v2_${userId}`,
  };
}

function resolveTourSteps(): DriveStep[] {
  return TOUR_TARGETS.flatMap((target) => {
    const element = document.querySelector(target.selector) ??
      (target.fallbackSelector ? document.querySelector(target.fallbackSelector) : null);

    if (!element) return [];

    return [{
      element,
      popover: {
        title: target.title,
        description: target.description,
        side: target.side ?? "right",
        align: target.align ?? "start",
      },
    }];
  });
}

export function usePremiumOnboarding() {
  const user = useMemo(() => storage.getUser(), []);
  const keys = useMemo(() => user ? getStorageKeys(user.id) : null, [user]);
  const [shouldShowWelcome, setShouldShowWelcome] = useState(false);

  const markSeen = useCallback(() => {
    if (!keys) return;
    localStorage.setItem(keys.current, "true");
    // Eski otomatik tur hook'u tekrar devreye girerse kullanıcıyı rahatsız etmesin.
    localStorage.setItem(keys.legacy, "true");
    setShouldShowWelcome(false);
  }, [keys]);

  const dismissOnboarding = useCallback(() => {
    markSeen();
  }, [markSeen]);

  const startTour = useCallback(() => {
    setShouldShowWelcome(false);

    window.setTimeout(() => {
      const steps = resolveTourSteps();
      if (steps.length === 0) {
        markSeen();
        return;
      }

      const driverObj = driver({
        steps,
        showProgress: true,
        animate: true,
        allowClose: true,
        allowKeyboardControl: true,
        overlayColor: "#172033",
        overlayOpacity: 0.28,
        overlayClickBehavior: "close",
        stagePadding: 8,
        stageRadius: 18,
        popoverClass: "orka-premium-tour",
        doneBtnText: "Bitir",
        nextBtnText: "İleri",
        prevBtnText: "Geri",
        progressText: "{{current}} / {{total}}",
        onDestroyed: () => {
          markSeen();
        },
      });

      driverObj.drive();
    }, TOUR_START_DELAY_MS);
  }, [markSeen]);

  useEffect(() => {
    if (!keys) return;
    if (localStorage.getItem(keys.current) === "true") return;

    const timer = window.setTimeout(() => {
      setShouldShowWelcome(true);
    }, WELCOME_DELAY_MS);

    return () => window.clearTimeout(timer);
  }, [keys]);

  return useMemo(() => ({
    shouldShowWelcome,
    startTour,
    dismissOnboarding,
    markSeen,
  }), [dismissOnboarding, markSeen, shouldShowWelcome, startTour]);
}
