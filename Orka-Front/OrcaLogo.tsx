/*
 * Design: "Sessiz Lüks" — Monochrome, stroke-only SVG.
 * Abstract orca dorsal fin / data-loop motif.
 * Inherits color from parent text-* class.
 */

interface OrcaLogoProps {
  className?: string;
}

export default function OrcaLogo({ className = "w-5 h-5" }: OrcaLogoProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
    >
      {/* Rising dorsal fin curve */}
      <path d="M4 20 C4 18 6 6 12 3 C18 6 20 18 20 20" />
      {/* Inner fin detail */}
      <path d="M8 20 C8 16 10 8 12 6 C14 8 16 16 16 20" opacity="0.4" />
      {/* Base wave */}
      <path d="M2 20 C6 18 10 17 12 17 C14 17 18 18 22 20" />
    </svg>
  );
}
