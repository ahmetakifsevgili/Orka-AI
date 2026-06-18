import { describe, expect, it } from "vitest";
import { statusTone, userSafeStatus } from "./userSafeStatus";

describe("userSafeStatus", () => {
  it("maps source basis statuses to user-safe labels", () => {
    expect(userSafeStatus("wiki_backed")).toBe("Wiki ile destekli");
    expect(userSafeStatus("source_grounded")).toBe("Kaynakla destekli");
    expect(userSafeStatus("evidence_insufficient")).not.toContain("_");
  });

  it("falls back without exposing raw separators for benign unknown statuses", () => {
    expect(userSafeStatus("custom_stage_state")).toBe("custom stage state");
    expect(userSafeStatus("custom-stage-state")).toBe("custom stage state");
  });

  it("does not expose internal provider, prompt, token, or debug markers", () => {
    expect(userSafeStatus("custom_provider_state")).toBe("Durum izleniyor");
    expect(userSafeStatus("openrouter_model_id_missing")).toBe("Durum izleniyor");
    expect(userSafeStatus("rawPrompt: developer payload")).toBe("Durum izleniyor");
  });

  it("classifies source statuses with stable tones", () => {
    expect(statusTone("wiki_backed")).toBe("good");
    expect(statusTone("source_grounded")).toBe("good");
    expect(statusTone("evidence_insufficient")).toBe("watch");
  });
});
