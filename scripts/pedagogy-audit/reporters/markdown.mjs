import fs from "node:fs/promises";
import path from "node:path";

export async function writeMarkdownReport(filePath, report) {
  const lines = [];
  lines.push("# Orka Pedagogical Quality Audit V2");
  lines.push("");
  lines.push(`**Run ID:** \`${report.runId}\``);
  lines.push(`**Target API:** \`${report.baseUrl}\``);
  lines.push(`**Provider Judge:** \`${report.includeAiProvider ? "requested" : "disabled"}\``);
  lines.push(`**Verdict:** ${report.releasePass ? "RELEASE PASS" : "RELEASE FAIL"}`);
  lines.push("");
  lines.push("This V2 report is based on typed evidence facts and deterministic invariants. Regex is used only as a privacy/safety backstop.");
  lines.push("");
  lines.push("## Scorecard");
  lines.push("");
  lines.push("| Persona | Contract | Diagnostic | Plan | Tutor | Remediation | Grounding/Privacy | Coherence | Total | Verdict |");
  lines.push("|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
  for (const persona of report.personas) {
    const s = persona.evaluation.scores;
    lines.push(`| \`${persona.personaId}\` | ${s.contract}/15 | ${s.diagnostic}/15 | ${s.plan}/20 | ${s.tutor_pedagogy}/25 | ${s.remediation}/10 | ${s.grounding_privacy}/10 | ${s.coherence}/5 | ${persona.evaluation.totalScore}/100 | ${persona.evaluation.releasePass ? "PASS" : "FAIL"} |`);
  }
  lines.push("");
  lines.push("## Issues");
  lines.push("");
  const issues = report.personas.flatMap((persona) => persona.evaluation.issues.map((issue) => ({ personaId: persona.personaId, ...issue })));
  if (issues.length === 0) {
    lines.push("No issues found.");
  } else {
    lines.push("| Persona | Area | Code | Severity | Message | Evidence Refs |");
    lines.push("|---|---|---|---|---|---|");
    for (const issue of issues) {
      lines.push(`| \`${issue.personaId}\` | ${issue.area} | \`${issue.code}\` | ${issue.severity} | ${escapeMd(issue.message)} | ${(issue.evidenceRefs ?? []).map((x) => `\`${escapeMd(x)}\``).join(", ")} |`);
    }
  }
  lines.push("");
  lines.push("## Evidence Surface Summary");
  lines.push("");
  for (const persona of report.personas) {
    const b = persona.bundle;
    const judge = persona.evaluation.judge ?? {};
    lines.push(`### ${persona.personaId}`);
    lines.push("");
    lines.push(`- Diagnostic: ${b.diagnostic.questionCount} questions, ${b.diagnostic.conceptDiversity} concept buckets, ${b.diagnostic.blankCount} blanks.`);
    lines.push(`- Plan: ${b.plan.chapterCount} chapters, ${b.plan.lessonCount} lessons, materialized=${b.plan.isMaterialized}, blockingIssues=${b.plan.blockingIssueCount}.`);
    lines.push(`- Tutor: trace=${b.tutor.traceIdPresent}, teachingMode=\`${b.tutor.teachingMode}\`, activeConcept=${b.tutor.activeConceptKey ? "`present`" : "`missing`"}, pedagogyCritical=${b.pedagogy.hasCriticalViolation}.`);
    lines.push(`- Wiki: ${b.wiki.pageCount} pages, ready=${b.wiki.readyPageCount}, degraded=${b.wiki.degradedPageCount}, skeleton=${b.wiki.skeletonPageCount}, pageQuestions=${b.wiki.pageQuestionCount}, pagePracticeReady=${b.wiki.pagePracticeReadyCount}, studyCards=${b.wiki.studyCardCount}.`);
    lines.push(`- Question bank: bound=${b.questionBank.systemBoundCount}, practiceReady=${b.questionBank.practiceReadyCount}, kgBound=${b.questionBank.practiceKgBoundCount}, practiceSubmit=${b.questionBank.practiceSubmitOk}, learningImpact=${b.questionBank.practiceLearningImpactCount}.`);
    lines.push(`- Mission: available=${b.mission.missionAvailable}, coach=${b.mission.coachAvailable}, conflicts=${b.mission.conflictWarningCount}.`);
    lines.push(`- Content judge: status=${judge.status ?? "unknown"}, used=${judge.llmJudgeUsed === true}, score=${judge.score ?? "n/a"}.`);
    lines.push("");
  }
  lines.push("## Endpoint Evidence Refs");
  lines.push("");
  for (const persona of report.personas) {
    lines.push(`### ${persona.personaId}`);
    lines.push("");
    lines.push("| Step | OK | Status | Parseable | Duration | Ref |");
    lines.push("|---|---:|---:|---:|---:|---|");
    for (const [key, endpoint] of Object.entries(persona.bundle.contract.endpoints)) {
      lines.push(`| \`${key}\` | ${endpoint.ok ? "yes" : "no"} | ${endpoint.status} | ${endpoint.parseable ? "yes" : "no"} | ${endpoint.durationMs}ms | \`${endpoint.ref}\` |`);
    }
    lines.push("");
  }
  lines.push("## Gate");
  lines.push("");
  lines.push("Persona pass requires >=85/100, every bucket >=75%, and no critical deterministic failure. Release pass requires every persona to pass.");
  lines.push("");
  lines.push(report.releasePass ? "Final result: release quality gate passed." : "Final result: release quality gate failed.");
  lines.push("");

  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, lines.join("\n"), "utf8");
}

function escapeMd(value) {
  return String(value ?? "").replace(/\|/g, "\\|").replace(/\n/g, " ");
}
