/**
 * ThemeContext — Koyu/Açık/Sistem tema yönetimi.
 * `data-theme="dark"|"light"` ve `class="dark"|"light"` document.documentElement'e uygulanır.
 * Tailwind `dark:` variant'ları için `class="dark"` zorunludur.
 */
import React, { createContext, useContext, useState, useEffect } from "react";

export type Theme = "Dark" | "Light" | "System";

interface ThemeContextType {
  theme: Theme;
  setTheme: (theme: Theme) => void;
  resolvedTheme: "dark" | "light"; // System çözümlenince gerçek değer
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

function applyTheme(resolved: "dark" | "light") {
  const root = document.documentElement;
  // Tailwind dark mode — class strategy
  root.classList.remove("dark", "light");
  root.classList.add(resolved);
  // data attribute (for custom CSS targeting)
  root.setAttribute("data-theme", resolved);
}

function resolveTheme(theme: Theme): "dark" | "light" {
  if (theme === "System") {
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }
  return theme === "Dark" ? "dark" : "light";
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() => {
    return (localStorage.getItem("orka_theme") as Theme) || "Dark";
  });
  const [resolvedTheme, setResolvedTheme] = useState<"dark" | "light">(() =>
    resolveTheme((localStorage.getItem("orka_theme") as Theme) || "Dark")
  );

  const setTheme = (newTheme: Theme) => {
    setThemeState(newTheme);
    localStorage.setItem("orka_theme", newTheme);
    const resolved = resolveTheme(newTheme);
    setResolvedTheme(resolved);
    applyTheme(resolved);
  };

  // Initial apply on mount
  useEffect(() => {
    applyTheme(resolveTheme(theme));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Listen for system theme changes when in System mode
  useEffect(() => {
    if (theme !== "System") return;
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = (e: MediaQueryListEvent) => {
      const resolved = e.matches ? "dark" : "light";
      setResolvedTheme(resolved);
      applyTheme(resolved);
    };
    mq.addEventListener("change", handler);
    return () => mq.removeEventListener("change", handler);
  }, [theme]);

  return (
    <ThemeContext.Provider value={{ theme, setTheme, resolvedTheme }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme() {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error("useTheme must be used within ThemeProvider");
  return ctx;
}
