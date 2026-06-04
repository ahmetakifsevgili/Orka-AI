import fs from "node:fs/promises";
import path from "node:path";

export async function writeJsonReport(filePath, report) {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
}
