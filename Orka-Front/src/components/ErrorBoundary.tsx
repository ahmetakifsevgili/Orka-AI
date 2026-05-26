import { Component, type ErrorInfo, type ReactNode } from "react";

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

/**
 * Global error boundary — yakalanmamış React hatalarını yakalar ve
 * beyaz ekran yerine kullanıcı dostu bir mesaj gösterir.
 */
export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error("[ErrorBoundary]", error, errorInfo);
  }

  handleRetry = () => {
    this.setState({ hasError: false, error: null });
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }
      return (
        <div className="flex h-screen w-screen items-center justify-center bg-zinc-950 text-zinc-400">
          <div className="flex flex-col items-center gap-4 max-w-md text-center px-6">
            <div className="flex h-16 w-16 items-center justify-center rounded-full bg-red-500/10 text-red-400">
              <svg className="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <circle cx="12" cy="12" r="10" />
                <line x1="12" y1="8" x2="12" y2="12" />
                <line x1="12" y1="16" x2="12.01" y2="16" />
              </svg>
            </div>
            <h2 className="text-lg font-bold text-zinc-200">Beklenmeyen bir hata oluştu</h2>
            <p className="text-sm text-zinc-500">
              Uygulama beklenmeyen bir hatayla karşılaştı. Sayfayı yenileyerek tekrar deneyebilirsiniz.
            </p>
            {this.state.error && (
              <pre className="mt-2 max-h-32 w-full overflow-auto rounded-lg bg-zinc-900 px-3 py-2 text-left text-[11px] text-zinc-600">
                {this.state.error.message}
              </pre>
            )}
            <div className="flex gap-3 mt-2">
              <button
                onClick={this.handleRetry}
                className="rounded-lg bg-sky-500/15 px-4 py-2 text-sm font-semibold text-sky-400 transition hover:bg-sky-500/25"
              >
                Tekrar dene
              </button>
              <button
                onClick={() => window.location.reload()}
                className="rounded-lg bg-zinc-800 px-4 py-2 text-sm font-semibold text-zinc-300 transition hover:bg-zinc-700"
              >
                Sayfayı yenile
              </button>
            </div>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
