import { useEffect } from "react";
import { driver } from "driver.js";
import "driver.js/dist/driver.css";
import "./PremiumOnboardingTour.css";
import { useAuth } from "@/contexts/AuthContext";
import { useLanguage } from "@/contexts/LanguageContext";

export function usePremiumOnboarding() {
  const { t, language } = useLanguage();
  const { user } = useAuth();

  useEffect(() => {
    if (!user) return;
    if (user.isOnboardingCompleted !== false) return;

    const storageKey = `orka_premium_tour_seen_v3_${user.id}`;

    const tourSeen = localStorage.getItem(storageKey);
    if (tourSeen === "true") return;

    const timer = setTimeout(() => {
      if (!document.getElementById("tour-new-topic")) return;

      const driverObj = driver({
        showProgress: true,
        animate: true,
        allowClose: true,
        doneBtnText: t("done"),
        nextBtnText: t("next"),
        prevBtnText: t("back"),
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
              title: t("onboarding_step_start_title"),
              description: t("onboarding_step_start_desc"),
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-dashboard",
            popover: {
              title: t("onboarding_step_dashboard_title"),
              description: t("onboarding_step_dashboard_desc"),
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-learning",
            popover: {
              title: t("onboarding_step_learning_title"),
              description: t("onboarding_step_learning_desc"),
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-wiki",
            popover: {
              title: t("onboarding_step_wiki_title"),
              description: t("onboarding_step_wiki_desc"),
              side: "right",
              align: "start",
            },
          },
          {
            element: "#tour-nav-ide",
            popover: {
              title: t("onboarding_step_ide_title"),
              description: t("onboarding_step_ide_desc"),
              side: "right",
              align: "start",
            },
          },
        ],
      });

      driverObj.drive();
    }, 1500);

    return () => clearTimeout(timer);
  }, [language, t, user?.id, user?.isOnboardingCompleted]);
}
