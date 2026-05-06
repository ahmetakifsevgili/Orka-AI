/**
 * RichMarkdown - V4 zengin içerik render katmanı.
 *
 * Destekler:
 *   1. GFM (tablolar, checkbox, strikethrough)
 *   2. LaTeX -> $...$ ve $...$ (KaTeX)
 *   3. Mermaid -> ```mermaid blokları
 *   4. Inline citation hover -> [text](url) link'leri 24x24 favicon + URL preview
 *   5. Pollinations görselleri normal markdown image olarak akar (img tag).
 *
 * Kullanım: <RichMarkdown content={...} className="prose prose-invert" />
 */import { useEffect, useRef, useId } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeKatex from "rehype-katex";
import "katex/dist/katex.min.css";

// Mermaid yalnızca gerçekten diagram render edileceğinde lazy yüklenir
let mermaidInitialized = false;
async function getMermaid() {
  const mermaid = (await import("mermaid")).default;
  if (mermaidInitialized) return mermaid;

  mermaid.initialize({
    startOnLoad: false,
    theme: "dark",
    securityLevel: "loose",
    fontFamily: "ui-sans-serif, system-ui, sans-serif",
    themeVariables: {
      primaryColor: "#10b981",
      primaryTextColor: "#e4e4e7",
      primaryBorderColor: "#3f3f46",
      lineColor: "#52525b",
      secondaryColor: "#27272a",
      tertiaryColor: "#18181b",
      background: "#09090b",
      mainBkg: "#18181b",
      secondBkg: "#27272a",
    },
  });
  mermaidInitialized = true;
  return mermaid;
}

function sanitizeMermaid(code: string) {
  return code.replace(/([A-Za-z0-9_]+)\[([^\]\n"]*[\(\):.;,][^\]\n"]*)\]/g, (_match, node, label) => {
    const escaped = String(label).replace(/"/g, '\\"');
    return `${node}["${escaped}"]`;
  });
}

function mermaidFallbackHtml(code: string) {
  const safeCode = code.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  return `
    <div class="rounded-xl border border-[#dcecf3] bg-white/75 p-4 text-xs text-[#667085]">
      <div class="mb-2 font-bold text-[#344054]">Diyagram metin olarak gösteriliyor</div>
      <div class="mb-3 leading-5">Mermaid bu çıktıyı güvenli şekilde çizemedi. İçerik kaybolmadı; kod bloğu olarak bırakıldı.</div>
      <pre class="max-h-80 overflow-auto rounded-lg bg-[#f7f9fa] p-3 text-[11px] leading-5 text-[#344054]">${safeCode}</pre>
    </div>
  `;
}

interface MermaidBlockProps {
  code: string;
}

function MermaidBlock({ code }: MermaidBlockProps) {
  const ref = useRef<HTMLDivElement>(null);
  const id = useId().replace(/:/g, "_");

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const mermaid = await getMermaid();
        let svg: string;
        try {
          const rendered = await mermaid.render(`m_${id}`, code.trim());
          svg = rendered.svg;
        } catch {
          const rendered = await mermaid.render(`m_${id}_safe`, sanitizeMermaid(code.trim()));
          svg = rendered.svg;
        }
        if (!cancelled && ref.current) ref.current.innerHTML = svg;
      } catch (err) {
        if (!cancelled && ref.current) {
          console.warn("Mermaid render fallback:", err);
          ref.current.innerHTML = mermaidFallbackHtml(code);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [code, id]);

  return (
    <div
      ref={ref}
      className="my-4 p-4 rounded-xl bg-zinc-950 border border-zinc-800/60 overflow-x-auto"
    />
  );
}

interface CitationLinkProps {
  href?: string;
  children: React.ReactNode;
  onSourceClick?: (sourceId: string, page: number) => void;
  onCitationClick?: (kind: "doc" | "wiki" | "web" | "external", ref: string) => void;
}

/**
 * Inline citation hover - http(s) link'leri tooltip'le sarar.
 * Eğer link bir resim (Pollinations vs.) değilse hostname'i favicon ile gösterir.
 */
function CitationLink({ href, children, onSourceClick, onCitationClick }: CitationLinkProps) {
  if (href?.startsWith("orka-source://")) {
    const match = href.match(/^orka-source:\/\/([^/]+)\/page\/(\d+)/);
    const sourceId = match?.[1];
    const page = Number(match?.[2] ?? 1);
    return (
      <button
        type="button"
        onClick={() => {
          if (!sourceId) return;
          onCitationClick?.("doc", `${sourceId}:p${page}`);
          onSourceClick?.(sourceId, page);
        }}
        className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full bg-amber-500/10 border border-amber-500/25 text-[11px] font-mono text-amber-300 hover:bg-amber-500/20 transition align-baseline"
        title={sourceId ? `Kaynak sayfa ${page}` : "Kaynak"}
      >
        {children}
      </button>
    );
  }

  if (href === "orka-wiki://local" || href === "orka-web://local") {
    const isWeb = href === "orka-web://local";
    return (
      <button
        type="button"
        onClick={() => onCitationClick?.(isWeb ? "web" : "wiki", isWeb ? "local-web" : "local-wiki")}
        className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full border text-[11px] font-mono align-baseline ${
          isWeb
            ? "bg-sky-500/10 border-sky-500/25 text-sky-300"
            : "bg-emerald-500/10 border-emerald-500/25 text-emerald-300"
        }`}
      >
        {children}
      </button>
    );
  }

  if (!href || !/^https?:\/\//i.test(href)) {
    return <a href={href}>{children}</a>;
  }

  let host = "";
  try {
    host = new URL(href).hostname.replace(/^www\./, "");
  } catch {
    host = href;
  }

  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      onClick={() => onCitationClick?.("external", href)}
      title={href}
      className="inline-flex items-center gap-1 text-emerald-400 hover:text-emerald-300 underline decoration-emerald-500/40 decoration-dotted underline-offset-2 transition"
    >
      {children}
      <span
        className="inline-flex items-center gap-1 ml-0.5 px-1.5 py-0.5 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-[10px] font-mono text-emerald-300/80 align-text-bottom"
        aria-hidden="true"
      >
        <img
          src={`https://www.google.com/s2/favicons?domain=${host}&sz=16`}
          alt=""
          className="w-3 h-3 rounded-sm"
          loading="lazy"
        />
        {host}
      </span>
    </a>
  );
}

interface RichMarkdownProps {
  content: string;
  className?: string;
  onSourceClick?: (sourceId: string, page: number) => void;
  onCitationClick?: (kind: "doc" | "wiki" | "web" | "external", ref: string) => void;
}

function withSourceLinks(content: string): string {
  return content
    .replace(/\[doc:([0-9a-fA-F-]{36}):p(\d+)\]/g, (_m, sourceId, page) =>
      `[doc:p${page}](orka-source://${sourceId}/page/${page})`
    )
    .replace(/\[wiki(?::[^\]]+)?\]/g, "[wiki](orka-wiki://local)")
    .replace(/\[web(?::[^\]]+)?\]/g, "[web](orka-web://local)");
}

export default function RichMarkdown({ content, className, onSourceClick, onCitationClick }: RichMarkdownProps) {
  return (
    <div className={className}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm, remarkMath]}
        rehypePlugins={[rehypeKatex]}
        components={{
          code({ className: codeCls, children, ...props }) {
            const match = /language-(\w+)/.exec(codeCls || "");
            const lang = match?.[1];
            const text = String(children).replace(/\n$/, "");

            if (lang === "mermaid") {
              return <MermaidBlock code={text} />;
            }

            // inline kod
            if (!lang) {
              return (
                <code className={codeCls} {...props}>
                  {children}
                </code>
              );
            }

            // Dil-tagged kod bloğu: varsayılan prose stilini koruruz
            return (
              <code className={codeCls} {...props}>
                {children}
              </code>
            );
          },
          a({ href, children }) {
            return <CitationLink href={href} onSourceClick={onSourceClick} onCitationClick={onCitationClick}>{children}</CitationLink>;
          },
          img({ src, alt }) {
            // Pollinations ve diğer external imajları sarma
            return (
              <img
                src={src}
                alt={alt}
                loading="lazy"
                className="my-4 rounded-xl border border-zinc-800/60 max-w-full"
              />
            );
          },
        }}
      >
        {withSourceLinks(content)}
      </ReactMarkdown>
    </div>
  );
}
