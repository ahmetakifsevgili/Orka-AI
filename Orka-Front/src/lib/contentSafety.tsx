import { ImageOff } from "lucide-react";
import type { ReactNode } from "react";
import type { Components } from "react-markdown";

const ALLOWED_IMAGE_HOSTS = new Set(["image.pollinations.ai"]);
const INTERNAL_PROTOCOLS = new Set(["orka-source:", "orka-wiki:", "orka-web:"]);

export const MAX_MERMAID_CHARS = 12_000;
export const MAX_MERMAID_LINES = 300;

export function isMermaidTooLarge(code: string) {
  return code.length > MAX_MERMAID_CHARS || code.split(/\r?\n/).length > MAX_MERMAID_LINES;
}

export function isSafeMarkdownUrl(href?: string) {
  if (!href) return false;
  if (href.startsWith("//")) return false;

  try {
    const parsed = new URL(href, window.location.origin);
    if (INTERNAL_PROTOCOLS.has(parsed.protocol)) return true;
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

export function isAllowedRemoteImage(src?: string) {
  if (!src || src.startsWith("//")) return false;

  try {
    const parsed = new URL(src, window.location.origin);
    return parsed.protocol === "https:" && ALLOWED_IMAGE_HOSTS.has(parsed.hostname.toLowerCase());
  } catch {
    return false;
  }
}

export function displayHost(href?: string) {
  if (!href) return "";
  try {
    return new URL(href).hostname.replace(/^www\./, "");
  } catch {
    return href;
  }
}

export function safeMarkdownUrlTransform(url: string) {
  return isSafeMarkdownUrl(url) ? url : "";
}

export function sanitizeMermaidSvg(svg: string) {
  if (typeof DOMParser === "undefined") return "";

  const document = new DOMParser().parseFromString(svg, "image/svg+xml");
  document.querySelectorAll("script, foreignObject, iframe, object, embed").forEach((node) => node.remove());

  document.querySelectorAll("*").forEach((node) => {
    for (const attr of Array.from(node.attributes)) {
      const name = attr.name.toLowerCase();
      const value = attr.value.trim();
      if (
        name.startsWith("on") ||
        ((name === "href" || name.endsWith(":href") || name === "src") && !isSafeSvgReference(value)) ||
        (name === "style" && /url\s*\(|expression\s*\(/i.test(value))
      ) {
        node.removeAttribute(attr.name);
      }
    }
  });

  const root = document.documentElement;
  return root?.tagName.toLowerCase() === "svg" ? root.outerHTML : "";
}

export function BlockedImagePlaceholder({ alt }: { alt?: string | null }) {
  return (
    <span className="my-4 flex max-w-full items-center gap-2 rounded-xl border border-[#dcecf3] bg-[#f7f9fa] px-4 py-3 text-xs text-[#667085]">
      <ImageOff className="h-4 w-4" aria-hidden="true" />
      {alt || "Uzak görsel güvenlik politikası nedeniyle gösterilmedi."}
    </span>
  );
}

export function SafeMarkdownLink({ href, children }: { href?: string; children?: ReactNode }) {
  if (!isSafeMarkdownUrl(href)) {
    return <span>{children}</span>;
  }

  const safeHref = href ?? "";
  return (
    <a href={safeHref} target="_blank" rel="noopener noreferrer nofollow">
      {children}
    </a>
  );
}

export function SafeMarkdownImage({ src, alt }: { src?: string; alt?: string }) {
  if (!isAllowedRemoteImage(src)) {
    return <BlockedImagePlaceholder alt={alt} />;
  }

  return <img src={src} alt={alt || ""} loading="lazy" className="my-4 max-w-full rounded-xl border border-[#dcecf3]" />;
}

export const safeMarkdownComponents: Components = {
  a({ href, children }) {
    return <SafeMarkdownLink href={href}>{children}</SafeMarkdownLink>;
  },
  img({ src, alt }) {
    return <SafeMarkdownImage src={src} alt={alt ?? undefined} />;
  },
};

function isSafeSvgReference(value: string) {
  return value.startsWith("#") && value.length > 1 && !value.includes("\n") && !value.includes("\r");
}
