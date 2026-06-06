import { useRef, useMemo, useEffect, useState } from "react";
import ForceGraph2D from "react-force-graph-2d";
import { Maximize2, Minimize2 } from "lucide-react";

export interface ObsidianGraphNode {
  id: string;
  label: string;
  group?: string; // e.g. "page", "concept"
  val?: number; // node size
  color?: string;
}

export interface ObsidianGraphLink {
  source: string;
  target: string;
  label?: string;
  color?: string;
}

export interface ObsidianGraphData {
  nodes: ObsidianGraphNode[];
  links: ObsidianGraphLink[];
}

interface ObsidianGraphProps {
  data: ObsidianGraphData;
  onNodeClick?: (node: ObsidianGraphNode) => void;
  className?: string;
}

export default function ObsidianGraph({ data, onNodeClick, className = "" }: ObsidianGraphProps) {
  const fgRef = useRef<any>(null);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const [dimensions, setDimensions] = useState({ width: 0, height: 0 });

  useEffect(() => {
    const observeTarget = containerRef.current;
    if (!observeTarget) return;

    const resizeObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setDimensions({
          width: entry.contentRect.width,
          height: entry.contentRect.height,
        });
      }
    });

    resizeObserver.observe(observeTarget);
    return () => resizeObserver.disconnect();
  }, []);

  // Force graph config
  useEffect(() => {
    if (fgRef.current && data.nodes.length > 0) {
      // Zoom to fit after a short delay so layout can settle
      setTimeout(() => {
        fgRef.current.zoomToFit(400);
      }, 500);
    }
  }, [data]);

  const toggleFullscreen = () => {
    setIsFullscreen(!isFullscreen);
  };

  const handleNodeClick = (node: any) => {
    // Zoom onto node
    if (fgRef.current) {
      fgRef.current.centerAt(node.x, node.y, 600);
      fgRef.current.zoom(4, 600);
    }
    onNodeClick?.(node as ObsidianGraphNode);
  };

  return (
    <div 
      ref={containerRef}
      className={`relative overflow-hidden bg-[#1e1e1e] rounded-xl border border-zinc-700/50 ${isFullscreen ? 'fixed inset-0 z-50 rounded-none m-0' : 'h-[500px] w-full ' + className}`}
    >
      {/* Overlay Toolbar */}
      <div className="absolute top-4 right-4 z-10 flex gap-2">
        <button 
          onClick={toggleFullscreen}
          className="p-2 rounded-lg bg-zinc-800/80 text-zinc-300 hover:bg-zinc-700 hover:text-white transition-colors backdrop-blur border border-zinc-700/50 shadow-lg"
          title={isFullscreen ? "Exit Fullscreen" : "Fullscreen"}
        >
          {isFullscreen ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
        </button>
      </div>

      <ForceGraph2D
        ref={fgRef}
        width={dimensions.width || 800}
        height={dimensions.height || 500}
        graphData={data}
        nodeLabel="label"
        nodeColor={(node: any) => node.color || "#8b5cf6"}
        linkColor={(link: any) => link.color || "rgba(156, 163, 175, 0.2)"}
        nodeRelSize={6}
        linkWidth={1.5}
        linkDirectionalArrowLength={3.5}
        linkDirectionalArrowRelPos={1}
        onNodeClick={handleNodeClick}
        nodeCanvasObject={(node: any, ctx, globalScale) => {
          const label = node.label;
          const fontSize = 12 / globalScale;
          ctx.font = `${fontSize}px Inter, sans-serif`;
          
          const textWidth = ctx.measureText(label).width;
          const bckgDimensions = [textWidth, fontSize].map(n => n + fontSize * 0.4); 

          ctx.fillStyle = 'rgba(24, 24, 27, 0.8)';
          if (node.id === "hovered") {
             ctx.fillStyle = 'rgba(24, 24, 27, 1)';
          }
          
          ctx.beginPath();
          ctx.arc(node.x, node.y, node.val || 4, 0, 2 * Math.PI, false);
          ctx.fillStyle = node.color || "#8b5cf6";
          ctx.fill();

          if (globalScale > 1.2) {
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillStyle = 'rgba(255, 255, 255, 0.9)';
            ctx.fillText(label, node.x, node.y + (node.val || 4) + fontSize);
          }
        }}
      />
    </div>
  );
}
