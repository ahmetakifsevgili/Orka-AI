import { describe, it, expect } from "vitest";
import {
  normalizeAppView,
  appViewPath,
  isKnownAppView,
  appViewLabels,
} from "./appNavigation";

describe("appNavigation", () => {
  describe("normalizeAppView", () => {
    it("should default to home when view is falsy", () => {
      expect(normalizeAppView(undefined)).toBe("home");
      expect(normalizeAppView(null)).toBe("home");
      expect(normalizeAppView("")).toBe("home");
    });

    it("should return the canonical view if it is already canonical", () => {
      expect(normalizeAppView("tutor")).toBe("tutor");
      expect(normalizeAppView("exams")).toBe("exams");
      expect(normalizeAppView("sources-wiki")).toBe("sources-wiki");
    });

    it("should resolve legacy aliases to their canonical views", () => {
      expect(normalizeAppView("dashboard")).toBe("home");
      expect(normalizeAppView("chat")).toBe("tutor");
      expect(normalizeAppView("classroom")).toBe("study-room");
      expect(normalizeAppView("learning")).toBe("review");
      expect(normalizeAppView("practice")).toBe("review");
      expect(normalizeAppView("central-exams")).toBe("exams");
      expect(normalizeAppView("wiki")).toBe("sources-wiki");
      expect(normalizeAppView("sources")).toBe("sources-wiki");
      expect(normalizeAppView("orkalm")).toBe("notebook");
      expect(normalizeAppView("ide")).toBe("code");
    });

    it("should default to home for unknown views", () => {
      expect(normalizeAppView("completely-unknown")).toBe("home");
    });
  });

  describe("appViewPath", () => {
    it("should return correct path for valid views", () => {
      expect(appViewPath("tutor")).toBe("/app/tutor");
      expect(appViewPath("home")).toBe("/app");
      expect(appViewPath("sources-wiki")).toBe("/app/sources");
    });

    it("should return default /app path for unknown views", () => {
      expect(appViewPath("completely-unknown")).toBe("/app");
    });
  });

  describe("isKnownAppView", () => {
    it("should return true for canonical views", () => {
      expect(isKnownAppView("home")).toBe(true);
      expect(isKnownAppView("tutor")).toBe(true);
    });

    it("should return true for legacy alias views", () => {
      expect(isKnownAppView("chat")).toBe(true);
      expect(isKnownAppView("dashboard")).toBe(true);
    });

    it("should return false for unknown views", () => {
      expect(isKnownAppView("completely-unknown")).toBe(false);
      expect(isKnownAppView(null)).toBe(false);
      expect(isKnownAppView(undefined)).toBe(false);
    });
  });

  describe("appViewLabels", () => {
    it("should return labels for all nav items", () => {
      const labels = appViewLabels();
      expect(labels).toContain("Ana Kokpit");
      expect(labels).toContain("Tutor");
      expect(labels).toContain("Study Room");
      expect(labels).toContain("Review / Quiz");
      expect(labels).toContain("Exam War Room");
      expect(labels).toContain("Sources / Wiki");
      expect(labels).toContain("Notebook Studio");
      expect(labels).toContain("Code IDE");
      expect(labels).toContain("Progress");
      expect(labels).toContain("Settings / Safety");
      expect(labels.length).toBe(10);
    });
  });
});
