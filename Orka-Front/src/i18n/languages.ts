export const SUPPORTED_LOCALES = [
  "tr",
  "en",
  "es",
  "fr",
  "de",
  "pt-BR",
  "it",
  "id",
  "nl",
  "pl",
] as const;

export type Locale = (typeof SUPPORTED_LOCALES)[number];

export interface LanguageOption {
  code: Locale;
  nativeName: string;
  englishName: string;
}

export const LANGUAGE_OPTIONS: LanguageOption[] = [
  { code: "tr", nativeName: "Türkçe", englishName: "Turkish" },
  { code: "en", nativeName: "English", englishName: "English" },
  { code: "es", nativeName: "Español", englishName: "Spanish" },
  { code: "fr", nativeName: "Français", englishName: "French" },
  { code: "de", nativeName: "Deutsch", englishName: "German" },
  { code: "pt-BR", nativeName: "Português BR", englishName: "Portuguese (Brazil)" },
  { code: "it", nativeName: "Italiano", englishName: "Italian" },
  { code: "id", nativeName: "Bahasa Indonesia", englishName: "Indonesian" },
  { code: "nl", nativeName: "Nederlands", englishName: "Dutch" },
  { code: "pl", nativeName: "Polski", englishName: "Polish" },
];

const LEGACY_TURKISH_MOJIBAKE = [0x54, 0xc3, 0xbc, 0x72, 0x6b, 0xc3, 0xa7, 0x65]
  .map((code) => String.fromCharCode(code))
  .join("");

export const normalizeLocale = (value?: string | null): Locale => {
  if (!value) return "tr";
  if (value === "Türkçe" || value === LEGACY_TURKISH_MOJIBAKE) return "tr";
  if (value === "English") return "en";
  const normalized = value.trim();
  return (SUPPORTED_LOCALES as readonly string[]).includes(normalized)
    ? (normalized as Locale)
    : "tr";
};
