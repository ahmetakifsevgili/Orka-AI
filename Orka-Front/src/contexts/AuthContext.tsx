import React, { createContext, useContext, useState, useEffect } from "react";
import {
  AuthAPI,
  UserAPI,
  storage,
  type AuthUser,
  type LoginRequest,
  type RegisterRequest,
} from "../services/api";
import { useTheme, type Theme } from "./ThemeContext";

interface AuthContextType {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isBootstrapping: boolean;
  login: (data: LoginRequest) => Promise<void>;
  register: (data: RegisterRequest) => Promise<void>;
  logout: () => Promise<void>;
  syncOnboardingCompleted: (updatedUser?: AuthUser) => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() => storage.getUser());
  const [isBootstrapping, setIsBootstrapping] = useState(true);
  const { setTheme } = useTheme();

  const persistUser = (nextUser: AuthUser) => {
    setUser(nextUser);
    localStorage.setItem("orka_user", JSON.stringify(nextUser));

    const serverTheme = nextUser.settings?.theme;
    if (serverTheme === "Dark" || serverTheme === "Light" || serverTheme === "System") {
      setTheme(serverTheme as Theme);
    }
  };

  useEffect(() => {
    async function bootstrap() {
      try {
        const token = storage.getToken();
        if (token) {
          // If we already have a token, fetch user profile to verify and sync
          const userRes = await UserAPI.getMe();
          persistUser(userRes.data);
        } else {
          // No token found, try silent refresh
          const refreshRes = await AuthAPI.refresh();
          const newToken = refreshRes.data.token;
          storage.saveToken(newToken);

          const userRes = await UserAPI.getMe();
          persistUser(userRes.data);
        }
      } catch (err) {
        // Clear storage if refresh/bootstrap fails
        storage.clear();
        setUser(null);
      } finally {
        setIsBootstrapping(false);
      }
    }
    bootstrap();
  }, []);

  const login = async (data: LoginRequest) => {
    const res = await AuthAPI.login(data);
    storage.save(res.data);
    persistUser(res.data.user);
  };

  const register = async (data: RegisterRequest) => {
    const res = await AuthAPI.register(data);
    storage.save(res.data);
    persistUser(res.data.user);
  };

  const logout = async () => {
    try {
      await AuthAPI.logout();
    } catch (err) {
      console.error("Logout request failed:", err);
    } finally {
      storage.clear();
      setUser(null);
    }
  };

  const syncOnboardingCompleted = (updatedUser?: AuthUser) => {
    if (updatedUser) {
      persistUser(updatedUser);
      return;
    }

    if (user) {
      persistUser({ ...user, isOnboardingCompleted: true });
    }
  };

  const isAuthenticated = !!user;

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated,
        isBootstrapping,
        login,
        register,
        logout,
        syncOnboardingCompleted,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return ctx;
}
