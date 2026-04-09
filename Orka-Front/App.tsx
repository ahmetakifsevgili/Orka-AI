import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import NotFound from "@/pages/NotFound";
import { Route, Switch } from "wouter";
import ErrorBoundary from "./components/ErrorBoundary";
import { ThemeProvider } from "./contexts/ThemeContext";
import { QuizHistoryProvider } from "./contexts/QuizHistoryContext";
import Landing from "./pages/Landing";
import Home from "./pages/Home";
import Profile from "./pages/Profile";
import QuizHistoryAndNotes from "./pages/QuizHistoryAndNotes";
import Login from "./pages/Login";
import Courses from "./pages/Courses";
import CourseDetail from "./pages/CourseDetail";

function Router() {
  return (
    <Switch>
      <Route path="/" component={Landing} />
      <Route path="/login" component={Login} />
      <Route path="/app" component={Home} />
      <Route path="/profile" component={Profile} />
      <Route path="/courses/:id" component={CourseDetail} />
      <Route path="/courses" component={Courses} />

      <Route path="/404" component={NotFound} />
      <Route component={NotFound} />
    </Switch>
  );
}

function App() {
  return (
    <ErrorBoundary>
      <ThemeProvider defaultTheme="dark">
        <QuizHistoryProvider>
          <TooltipProvider>
            <Toaster
              theme="dark"
              toastOptions={{
                style: {
                  background: "#18181b",
                  border: "1px solid #27272a",
                  color: "#fafafa",
                },
              }}
            />
            <Router />
          </TooltipProvider>
        </QuizHistoryProvider>
      </ThemeProvider>
    </ErrorBoundary>
  );
}

export default App;
