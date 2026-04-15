/**
 * FontSizeContext — Küçük/Orta/Büyük font ayarı.
 * document.documentElement'e --font-scale CSS custom property ekler.
 */
import React, { createContext, useContext, useState, useEffect } from "react";

export type FontSize = "Small" | "Medium" | "Large";

interface FontSizeContextType {
  fontSize: FontSize;
  setFontSize: (size: FontSize) => void;
}

const FontSizeContext = createContext<FontSizeContextType | undefined>(undefined);

const SCALE_MAP: Record<FontSize, string> = {
  Small: "0.875",
  Medium: "1",
  Large: "1.125",
};

function applyFontSize(size: FontSize) {
  document.documentElement.style.setProperty("--font-scale", SCALE_MAP[size]);
  document.documentElement.setAttribute("data-font-size", size.toLowerCase());
}

export function FontSizeProvider({ children }: { children: React.ReactNode }) {
  const [fontSize, setFontSizeState] = useState<FontSize>(() => {
    return (localStorage.getItem("orka_font_size") as FontSize) || "Medium";
  });

  const setFontSize = (size: FontSize) => {
    setFontSizeState(size);
    localStorage.setItem("orka_font_size", size);
    applyFontSize(size);
  };

  useEffect(() => {
    applyFontSize(fontSize);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <FontSizeContext.Provider value={{ fontSize, setFontSize }}>
      {children}
    </FontSizeContext.Provider>
  );
}

export function useFontSize() {
  const ctx = useContext(FontSizeContext);
  if (!ctx) throw new Error("useFontSize must be used within FontSizeProvider");
  return ctx;
}
