import { type ReactNode } from "react";
import { Route, Switch, Redirect } from "wouter";
import { Toaster } from "react-hot-toast";
import { QuizHistoryProvider } from "./contexts/QuizHistoryContext";
import { LanguageProvider } from "./contexts/LanguageContext";
import { ThemeProvider } from "./contexts/ThemeContext";
import { FontSizeProvider } from "./contexts/FontSizeContext";
import Landing from "./pages/Landing";
import Login from "./pages/Login";
import Home from "./pages/Home";
import Profile from "./pages/Profile";
import Courses from "./pages/Courses";
import NotFound from "./pages/NotFound";

// ── ProtectedRoute ─────────────────────────────────────────────────────────
// Token yoksa /login'e yönlendirir. Render sırasında senkron kontrol → flash yok.

function ProtectedRoute({ children }: { children: ReactNode }) {
  if (!localStorage.getItem("orka_token")) {
    return <Redirect to="/login" />;
  }
  return <>{children}</>;
}

// ── Router ─────────────────────────────────────────────────────────────────

function AppRouter() {
  return (
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
  );
}

// ── App ────────────────────────────────────────────────────────────────────

export default function App() {
  return (
    <ThemeProvider>
      <FontSizeProvider>
        <LanguageProvider>
          <QuizHistoryProvider>
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
          </QuizHistoryProvider>
        </LanguageProvider>
      </FontSizeProvider>
    </ThemeProvider>
  );
}
