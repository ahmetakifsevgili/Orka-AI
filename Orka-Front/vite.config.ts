import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "path";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 3000,
    strictPort: true,
    proxy: {
      "/api": {
        target: process.env.VITE_API_PROXY_TARGET ?? "http://localhost:5065",
        changeOrigin: true,
        secure: false,
      },
    },
  },
  build: {
    chunkSizeWarningLimit: 2300,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes("node_modules")) return undefined;
          if (id.includes("mermaid")) return "vendor-mermaid";
          if (id.includes("cytoscape")) return "vendor-graph";
          if (id.includes("monaco-editor") || id.includes("@monaco-editor")) return "vendor-code-editor";
          if (id.includes("framer-motion")) return "vendor-motion";
          return undefined;
        },
      },
    },
  },
});
