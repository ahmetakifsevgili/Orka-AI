import React from "react";
import { Redirect } from "wouter";
import { useAuth } from "../contexts/AuthContext";
import OrcaLogo from "./OrcaLogo";

interface PublicGateProps {
  children: React.ReactNode;
}

export default function PublicGate({ children }: PublicGateProps) {
  const { user, isAuthenticated, isBootstrapping } = useAuth();

  if (isBootstrapping) {
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
          <span className="ml-2">Yükleniyor...</span>
        </div>
      </div>
    );
  }

  if (isAuthenticated && user) {
    return <Redirect to="/app" />;
  }

  return <>{children}</>;
}
