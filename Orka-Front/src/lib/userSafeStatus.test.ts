import { describe, expect, it } from "vitest";
import { statusTone, userSafeStatus } from "./userSafeStatus";

describe("userSafeStatus", () => {
  it("maps source basis statuses to user-safe labels", () => {
    expect(userSafeStatus("wiki_backed")).toBe("Wiki ile destekli");
    expect(userSafeStatus("source_grounded")).toBe("Kaynakla destekli");
    expect(userSafeStatus("evidence_insufficient")).not.toContain("_");
  });

  it("falls back without exposing raw separators for unknown statuses", () => {
    expect(userSafeStatus("custom_provider_state")).toBe("custom provider state");
    expect(userSafeStatus("custom-provider-state")).toBe("custom provider state");
  });

  it("classifies source statuses with stable tones", () => {
    expect(statusTone("wiki_backed")).toBe("good");
    expect(statusTone("source_grounded")).toBe("good");
    expect(statusTone("evidence_insufficient")).toBe("watch");
  });
});
