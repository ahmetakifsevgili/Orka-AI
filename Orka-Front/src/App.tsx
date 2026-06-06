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
import OrcaLogo from "./components/OrcaLogo";

const Landing = lazy(() => import("./pages/Landing"));
const Login = lazy(() => import("./pages/Login"));
const Home = lazy(() => import("./pages/Home"));
const Profile = lazy(() => import("./pages/Profile"));
const Courses = lazy(() => import("./pages/Courses"));
const NotFound = lazy(() => import("./pages/NotFound"));

function OrkaLoadingScreen({ label = "Yükleniyor" }: { label?: string }) {
  return (
    <div className="flex h-screen w-screen flex-col items-center justify-center gap-4" style={{ background: "#070809" }}>
      <div className="relative">
        <div
          className="absolute inset-0 rounded-2xl blur-xl"
          style={{ background: "rgba(110, 215, 206, 0.18)" }}
        />
        <div
          className="relative grid h-12 w-12 place-items-center rounded-2xl"
          style={{ background: "#6ed7ce" }}
        >
          <OrcaLogo className="h-6 w-6" style={{ color: "#041210" }} />
        </div>
      </div>
      <div className="flex items-center gap-2" style={{ color: "#5a6360", fontSize: "12px", fontWeight: 500 }}>
        <span
          className="inline-block h-1 w-1 rounded-full animate-bounce"
          style={{ background: "#6ed7ce", animationDelay: "0ms" }}
        />
        <span
          className="inline-block h-1 w-1 rounded-full animate-bounce"
          style={{ background: "#6ed7ce", animationDelay: "150ms" }}
        />
        <span
          className="inline-block h-1 w-1 rounded-full animate-bounce"
          style={{ background: "#6ed7ce", animationDelay: "300ms" }}
        />
        <span className="ml-2">{label}</span>
      </div>
    </div>
  );
}

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
    return <OrkaLoadingScreen label="Oturum doğrulanıyor" />;
  }

  if (!isAuthenticated) {
    return <Redirect to="/login" />;
  }

  return <>{children}</>;
}

// ── Router ─────────────────────────────────────────────────────────────────

function AppRouter() {
  return (
    <Suspense fallback={<OrkaLoadingScreen />}>
      <Switch>
        {/* Public */}
        <Route path="/" component={Landing} />
        <Route path="/login" component={Login} />

        {/* Protected */}
        <Route path="/app/:view">
          {(params) => (
            <ProtectedRoute>
              <Home initialView={params.view} />
            </ProtectedRoute>
          )}
        </Route>
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
