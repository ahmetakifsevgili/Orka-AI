import { useState } from "react";
import { Check, Copy, Download, Printer } from "lucide-react";
import toast from "react-hot-toast";
import { KorteksAPI } from "@/services/api";

interface ResearchToolbarProps {
  content: string;
  topic?: string;
}

function sanitizeFilename(name: string): string {
  return (
    name
      .replace(/[^a-zA-Z0-9ğüşçıöçĞÜŞİÖÇ _-]/g, "")
      .trim()
      .replace(/\s+/g, "_") || "korteks-rapor"
  );
}

export default function ResearchToolbar({ content, topic }: ResearchToolbarProps) {
  const [copied, setCopied] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);
  const title = topic?.trim() || "Korteks Araştırması";

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(content);
      setCopied(true);
      toast.success("Rapor panoya kopyalandı");
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error("Panoya kopyalanamadı");
    }
  };

  const handleMarkdownDownload = () => {
    const blob = new Blob([content], { type: "text/markdown;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${sanitizeFilename(title)}.md`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    toast.success("Markdown dosyası indirildi");
  };

  const handlePdf = async () => {
    if (pdfLoading) return;
    setPdfLoading(true);
    try {
      const response = await KorteksAPI.exportHtml({ topic: title, markdown: content });
      if (!response.ok) throw new Error("Export başarısız");
      const html = await response.text();
      const win = window.open("", "_blank", "width=900,height=1100");
      if (!win) {
        toast.error("Tarayıcı yeni pencereyi engelledi. Pop-up izni ver.");
        return;
      }
      win.document.open();
      win.document.write(html);
      win.document.close();
      toast.success("Yazdırma penceresi açıldı");
    } catch (error) {
      console.error(error);
      toast.error("PDF oluşturulamadı");
    } finally {
      setPdfLoading(false);
    }
  };

  const buttonClass =
    "flex items-center gap-1.5 rounded-lg border soft-border px-3 py-1.5 text-[12px] font-medium soft-text-muted transition-colors hover:bg-surface-muted hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50";

  return (
    <div className="mt-4 flex flex-wrap items-center gap-2 border-t soft-border pt-3">
      <span className="mr-1 text-[11px] font-medium uppercase tracking-wide soft-text-muted">Raporu dışa aktar</span>
      <button onClick={handlePdf} disabled={pdfLoading} className={buttonClass} title="PDF olarak kaydet">
        <Printer className="h-3.5 w-3.5" />
        {pdfLoading ? "Hazırlanıyor..." : "PDF"}
      </button>
      <button onClick={handleMarkdownDownload} className={buttonClass}>
        <Download className="h-3.5 w-3.5" />
        Markdown
      </button>
      <button onClick={handleCopy} className={buttonClass}>
        {copied ? <Check className="h-3.5 w-3.5 text-emerald-600" /> : <Copy className="h-3.5 w-3.5" />}
        {copied ? "Kopyalandı" : "Kopyala"}
      </button>
    </div>
  );
}
