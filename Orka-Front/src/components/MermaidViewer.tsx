import React, { useEffect, useRef, useState } from 'react';
import mermaid from 'mermaid';

mermaid.initialize({
  startOnLoad: false,
  theme: 'dark',
  fontFamily: 'Outfit, Inter, sans-serif'
});

export default function MermaidViewer({ chart }: { chart: string }) {
  const [svgStr, setSvgStr] = useState<string>('');

  useEffect(() => {
    let isMounted = true;
    const renderChart = async () => {
      try {
        const id = `mermaid-${Math.random().toString(36).substr(2, 9)}`;
        const { svg } = await mermaid.render(id, chart);
        if (isMounted) setSvgStr(svg);
      } catch (err) {
        console.error('Mermaid render error:', err);
        if (isMounted) {
          setSvgStr(`<div class="text-amber-700 text-xs text-center border border-amber-500/20 bg-amber-500/10 p-2 rounded">Diyagram çizilemedi: Sentaks Hatası</div>`);
        }
      }
    };
    if (chart) {
      renderChart();
    }
    return () => {
      isMounted = false;
    };
  }, [chart]);

  return (
    <div 
      className="my-4 soft-surface soft-border p-4 rounded-xl flex items-center justify-center overflow-x-auto w-full transition-all duration-300"
      dangerouslySetInnerHTML={{ __html: svgStr }} 
    />
  );
}
