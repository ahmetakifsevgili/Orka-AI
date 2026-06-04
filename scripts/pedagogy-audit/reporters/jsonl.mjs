import fs from "node:fs/promises";
import path from "node:path";

export async function writeJsonlReport(filePath, report) {
  const lines = [];
  for (const persona of report.personas) {
    lines.push(JSON.stringify({ type: "persona", personaId: persona.personaId, totalScore: persona.evaluation.totalScore, releasePass: persona.evaluation.releasePass }));
    for (const invariant of persona.evaluation.invariants) {
      lines.push(JSON.stringify({ type: "invariant", personaId: persona.personaId, ...invariant }));
    }
    for (const [key, endpoint] of Object.entries(persona.bundle.contract.endpoints)) {
      lines.push(JSON.stringify({ type: "endpoint", personaId: persona.personaId, key, ...endpoint }));
    }
  }
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, `${lines.join("\n")}\n`, "utf8");
}
