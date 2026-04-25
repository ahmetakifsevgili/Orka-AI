/*
 * MarkdownRender — KaTeX + GFM + image override'larını içeren
 * tek noktadan Markdown renderer. ChatMessage, QuizCard ve Wiki
 * bileşenlerinde aynı davranışı garantiler.
 *
 * Kullanım:
 *   <MarkdownRender>{text}</MarkdownRender>             // blok (paragraf, h1..h6, code block)
 *   <MarkdownRender inline>{text}</MarkdownRender>      // satır içi (paragraf unwrap, margin yok)
 */

import { memo, useState, useCallback } from "react";
import ReactMarkdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeKatex from "rehype-katex";
import { Prism as SyntaxHighlighter } from "react-syntax-highlighter";
import { vscDarkPlus } from "react-syntax-highlighter/dist/esm/styles/prism";
import { Check, Copy } from "lucide-react";
import MermaidViewer from "./MermaidViewer";

interface MarkdownRenderProps {
  children: string;
  /** true => paragrafı unwrap et, margin/padding sıfır. Quiz seçenekleri için. */
  inline?: boolean;
  /** Ek components override'ları (code block vs için). */
  components?: Components;
}

const imgRender: Components["img"] = ({ src, alt }) => (
  <figure className="my-4">
    <img
      src={src}
      alt={alt || ""}
      loading="lazy"
      className="max-w-full h-auto rounded-lg border border-zinc-700/60 bg-zinc-950"
      onError={(e) => {
        (e.currentTarget as HTMLImageElement).style.display = "none";
      }}
    />
    {alt && (
      <figcaption className="text-[11px] text-zinc-500 mt-1.5 text-center italic">
        {alt}
      </figcaption>
    )}
  </figure>
);

const inlineImgRender: Components["img"] = ({ src, alt }) => (
  <img
    src={src}
    alt={alt || ""}
    loading="lazy"
    className="inline-block max-h-24 w-auto rounded border border-zinc-700/60 mx-1 align-middle"
    onError={(e) => {
      (e.currentTarget as HTMLImageElement).style.display = "none";
    }}
  />
);

function CodeBlock({
  children,
  className,
}: {
  children: React.ReactNode;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);
  const language = className?.replace("language-", "") || "text";
  const codeText =
    typeof children === "string"
      ? children.replace(/\n$/, "")
      : String(children).replace(/\n$/, "");

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(codeText).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [codeText]);

  if (language === "mermaid") {
    return <MermaidViewer chart={codeText} />;
  }

  return (
    <div className="relative my-4 rounded-xl overflow-hidden border border-zinc-700/60 bg-[#1e1e1e]">
      <div className="flex items-center justify-between px-4 py-2 bg-zinc-800/80 border-b border-zinc-700/40">
        <span className="text-[11px] font-mono text-zinc-400 uppercase tracking-wider">
          {language}
        </span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1.5 text-[11px] text-zinc-400 hover:text-zinc-100 transition-colors duration-150 px-2 py-0.5 rounded hover:bg-zinc-700/50"
        >
          {copied ? (
            <><Check className="w-3.5 h-3.5 text-emerald-400" /><span className="text-emerald-400">Kopyalandı</span></>
          ) : (
            <><Copy className="w-3.5 h-3.5" /><span>Kopyala</span></>
          )}
        </button>
      </div>
      <SyntaxHighlighter
        language={language}
        style={vscDarkPlus}
        customStyle={{
          margin: 0,
          padding: "1rem",
          background: "#1e1e1e",
          fontSize: "13px",
          lineHeight: "1.6",
          borderRadius: 0,
        }}
        codeTagProps={{ style: { fontFamily: "'JetBrains Mono', monospace" } }}
      >
        {codeText}
      </SyntaxHighlighter>
    </div>
  );
}

function InlineCode({ children }: { children: React.ReactNode }) {
  return (
    <code className="text-zinc-300 bg-zinc-800 px-1.5 py-0.5 rounded text-xs font-mono">
      {children}
    </code>
  );
}

function MarkdownRenderInner({ children, inline = false, components }: MarkdownRenderProps) {
  const inlineComponents: Components = {
    p: ({ children }) => <>{children}</>,
    img: inlineImgRender,
    ...components,
  };

  const blockComponents: Components = {
    img: imgRender,
    code({ className, children }) {
      const isBlock = className?.startsWith("language-") || (typeof children === "string" && children.includes("\n"));
      return isBlock ? (
        <CodeBlock className={className}>{children}</CodeBlock>
      ) : (
        <InlineCode>{children}</InlineCode>
      );
    },
    pre: ({ children }) => <>{children}</>,
    ...components,
  };

  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm, remarkMath]}
      rehypePlugins={[rehypeKatex]}
      components={inline ? inlineComponents : blockComponents}
    >
      {children}
    </ReactMarkdown>
  );
}

export default memo(MarkdownRenderInner);
