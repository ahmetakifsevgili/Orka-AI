import { useLocation } from "wouter";

export default function NotFound() {
  const [, navigate] = useLocation();
  return (
    <div className="h-screen flex items-center justify-center bg-zinc-950">
      <div className="text-center">
        <p className="text-6xl font-semibold text-zinc-700 mb-4">404</p>
        <p className="text-sm text-zinc-500 mb-6">Sayfa bulunamadı.</p>
        <button
          onClick={() => navigate("/")}
          className="text-xs text-zinc-400 hover:text-zinc-200 underline underline-offset-4 transition-colors"
        >
          Ana sayfaya dön
        </button>
      </div>
    </div>
  );
}
