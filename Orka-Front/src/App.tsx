import { type ReactNode, useState, useEffect, lazy, Suspense } from "react";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { Route, Switch, Redirect } from "wouter";
import { Toaster } from "react-hot-toast";
import { QuizHistoryProvider } from "./contexts/QuizHistoryContext";
import { LanguageProvider } from "./contexts/LanguageContext";
import { ThemeProvider } from "./contexts/ThemeContext";
import { FontSizeProvider } from "./contexts/FontSizeContext";
import { ToolCapabilitiesProvider } from "./contexts/ToolCapabilitiesContext";
import { AuthAPI } from "./services/api";

const Landing = lazy(() => import("./pages/Landing"));
const Login = lazy(() => import("./pages/Login"));
const Home = lazy(() => import("./pages/Home"));
const Profile = lazy(() => import("./pages/Profile"));
const Courses = lazy(() => import("./pages/Courses"));
const NotFound = lazy(() => import("./pages/NotFound"));

// ── ProtectedRoute ─────────────────────────────────────────────────────────
// Token yoksa /login'e yönlendirir. Refresh token varsa sessizce yenilemeyi dener.

function ProtectedRoute({ children }: { children: ReactNode }) {
  const [isBootstrapping, setIsBootstrapping] = useState(true);
  const [isAuthenticated, setIsAuthenticated] = useState(false);

  useEffect(() => {
    async function checkAuth() {
      const token = localStorage.getItem("orka_token");
      if (token) {
        setIsAuthenticated(true);
        setIsBootstrapping(false);
        return;
      }

      try {
        const response = await AuthAPI.refresh();
        const data = response.data;
        localStorage.setItem("orka_token", data.token);
        setIsAuthenticated(true);
      } catch (err) {
        localStorage.removeItem("orka_token");
        setIsAuthenticated(false);
      } finally {
        setIsBootstrapping(false);
      }
    }
    checkAuth();
  }, []);

  if (isBootstrapping) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-zinc-950 text-zinc-400">
        <div className="flex flex-col items-center gap-3">
          <svg className="h-8 w-8 animate-spin text-sky-500" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider">Oturum doğrulanıyor...</span>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Redirect to="/login" />;
  }

  return <>{children}</>;
}

// ── Router ─────────────────────────────────────────────────────────────────

function AppRouter() {
  return (
    <Suspense fallback={
      <div className="flex h-screen w-screen items-center justify-center bg-zinc-950 text-zinc-400">
        <div className="flex flex-col items-center gap-3">
          <svg className="h-8 w-8 animate-spin text-sky-500" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider">Yükleniyor...</span>
        </div>
      </div>
    }>
      <Switch>
        {/* Public */}
        <Route path="/" component={Landing} />
        <Route path="/login" component={Login} />

        {/* Protected */}
        <Route path="/app">
          <ProtectedRoute>
            <Home />
          </ProtectedRoute>
        </Route>
        <Route path="/profile">
          <ProtectedRoute>
            <Profile />
          </ProtectedRoute>
        </Route>
        <Route path="/courses">
          <ProtectedRoute>
            <Courses />
          </ProtectedRoute>
        </Route>

        <Route component={NotFound} />
      </Switch>
    </Suspense>
  );
}

// ── App ────────────────────────────────────────────────────────────────────

export default function App() {
  return (
    <ErrorBoundary>
    <ThemeProvider>
      <FontSizeProvider>
        <LanguageProvider>
          <QuizHistoryProvider>
            <ToolCapabilitiesProvider>
              <Toaster
                position="top-right"
                toastOptions={{
                  style: {
                    background: "#18181b",
                    border: "1px solid #27272a",
                    color: "#fafafa",
                    fontSize: "13px",
                  },
                }}
              />
              <AppRouter />
            </ToolCapabilitiesProvider>
          </QuizHistoryProvider>
        </LanguageProvider>
      </FontSizeProvider>
    </ThemeProvider>
    </ErrorBoundary>
  );
}
