import { useEffect, useId, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeKatex from "rehype-katex";
import "katex/dist/katex.min.css";
import {
  BlockedImagePlaceholder,
  displayHost,
  isAllowedRemoteImage,
  isMermaidTooLarge,
  isSafeMarkdownUrl,
  safeMarkdownUrlTransform,
  sanitizeMermaidSvg,
} from "@/lib/contentSafety";

let mermaidInitialized = false;

async function getMermaid() {
  const mermaid = (await import("mermaid")).default;
  if (mermaidInitialized) return mermaid;

  mermaid.initialize({
    startOnLoad: false,
    theme: "dark",
    securityLevel: "strict",
    htmlLabels: false,
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

function MermaidFallback({ code }: { code: string }) {
  return (
    <div className="my-4 rounded-xl border border-[#dcecf3] bg-white/75 p-4 text-xs text-[#667085]">
      <div className="mb-2 font-bold text-[#344054]">Diyagram metin olarak gösteriliyor</div>
      <div className="mb-3 leading-5">Mermaid bu çıktıyı güvenli şekilde çizemedi. İçerik kaybolmadı; kod bloğu olarak bırakıldı.</div>
      <pre className="max-h-80 overflow-auto rounded-lg bg-[#f7f9fa] p-3 text-[11px] leading-5 text-[#344054]">{code}</pre>
    </div>
  );
}

function MermaidBlock({ code }: { code: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const id = useId().replace(/:/g, "_");
  const [fallback, setFallback] = useState(false);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        if (isMermaidTooLarge(code)) {
          setFallback(true);
          return;
        }

        const mermaid = await getMermaid();
        let svg: string;
        try {
          const rendered = await mermaid.render(`m_${id}`, code.trim());
          svg = sanitizeMermaidSvg(rendered.svg);
        } catch {
          const rendered = await mermaid.render(`m_${id}_safe`, sanitizeMermaid(code.trim()));
          svg = sanitizeMermaidSvg(rendered.svg);
        }

        if (!svg) throw new Error("Mermaid SVG did not pass sanitization.");
        if (!cancelled && ref.current) {
          ref.current.innerHTML = svg;
          setFallback(false);
        }
      } catch (err) {
        if (!cancelled) {
          console.warn("Mermaid render fallback:", err);
          setFallback(true);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [code, id]);

  return fallback ? (
    <MermaidFallback code={code} />
  ) : (
    <div
      ref={ref}
      className="my-4 rounded-xl border border-zinc-800/60 bg-zinc-950 p-4 overflow-x-auto"
    />
  );
}

interface CitationLinkProps {
  href?: string;
  children: React.ReactNode;
  onSourceClick?: (sourceId: string, page: number) => void;
  onCitationClick?: (kind: "doc" | "wiki" | "web" | "external", ref: string) => void;
}

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

  if (!isSafeMarkdownUrl(href)) {
    return <span>{children}</span>;
  }

  const safeHref = href ?? "";
  const host = displayHost(safeHref);
  return (
    <a
      href={safeHref}
      target="_blank"
      rel="noopener noreferrer nofollow"
      onClick={() => onCitationClick?.("external", safeHref)}
      title={safeHref}
      className="inline-flex items-center gap-1 text-emerald-400 hover:text-emerald-300 underline decoration-emerald-500/40 decoration-dotted underline-offset-2 transition"
    >
      {children}
      <span
        className="inline-flex items-center gap-1 ml-0.5 px-1.5 py-0.5 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-[10px] font-mono text-emerald-300/80 align-text-bottom"
        aria-hidden="true"
      >
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
        urlTransform={safeMarkdownUrlTransform}
        components={{
          code({ className: codeCls, children, ...props }) {
            const match = /language-(\w+)/.exec(codeCls || "");
            const lang = match?.[1];
            const text = String(children).replace(/\n$/, "");

            if (lang === "mermaid") {
              return <MermaidBlock code={text} />;
            }

            if (!lang) {
              return (
                <code className={codeCls} {...props}>
                  {children}
                </code>
              );
            }

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
            if (!isAllowedRemoteImage(src)) {
              return <BlockedImagePlaceholder alt={alt} />;
            }

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
