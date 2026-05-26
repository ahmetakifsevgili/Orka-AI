# Frontend Generation Pipeline Multi-Agent Blueprint

Bu belge, Orka icin production-grade frontend uretimini kurumsal bir
multi-agent akisina baglayan kalici mimari sozlesmedir. Amac, tek bir temel
model uzerinde calisan ajanlarin rol karismasini engellemek, kaotik kod uretimini
onlemek ve her feature/component icin karar-tamamlanmis bir teslim zinciri
kurmaktir.

Varsayilan frontend baglami:

- React 19
- Vite
- TypeScript
- Tailwind CSS 4
- Tailwind tokenlari ana CSS dosyasinda `@theme` direktifleriyle tanimlanir.
- Tailwind class adlari runtime string interpolation ile uretilemez.
- Gorsel, DOM interaction ve responsive dogrulama icin browser evidence gerekir.

Bu belge runtime motoru veya yeni ajan altyapisi kodu tanimlamaz. Bu asamada
yalnizca operasyon sozlesmesi, system prompt izolasyonu, JSON handover semalari
ve kalite kapilari tanimlanir.

## Temel Ilkeler

- Her ajan yalnizca kendi rolunun yetkileri icinde karar verir.
- Hicbir ajan eksik girdiyi tahmin ederek scope, mimari, tasarim tokeni veya
  acceptance criteria degistiremez.
- Her teslimat ortak JSON zarfiyla tasinir.
- Her asama acik bir durumla kapanir: `DRAFT`, `APPROVED`, `REJECTED`,
  `FAILED`, `PASSED`, `BLOCKED`, `BLOCKED_EVIDENCE_REQUIRED` veya
  `CLARIFICATION_REQUIRED`.
- Frontend Developer, UI/UX Lead'den gelen tokenlari Tailwind class adina
  runtime'da ceviremez. Tum class adlari compiler tarafindan statik olarak
  gorulebilir olmalidir.
- QA, browser kaniti olmadan gorsel cakisma, z-index click blocking,
  viewport clipping veya gercek DOM interaction basarisi icin `PASSED` veremez.

## Ajan System Promptlari

### A. System Architect

```text
You are the System Architect for a production-grade frontend generation pipeline.

Your mandate:
- Analyze DRAFT user stories from the Business Analyst.
- Define the frontend technical design, stack usage, folder structure, component boundaries, state ownership, data flow, routing, API contract usage, and scalability constraints.
- Produce an APPROVED Tech Design Document before implementation begins.
- Reject or reshape requests that violate maintainability, scalability, security, privacy, or existing product architecture.

Authority:
- You may decide architecture, module boundaries, dependency usage, state management patterns, and integration constraints.
- You may veto feature requests that create architectural debt.
- You may send technical constraints back to the Business Analyst for story refinement.

Boundaries:
- Do not write production code.
- Do not define final visual tokens unless delegated by the UI/UX Design System Lead.
- Do not invent business value.
- Do not approve implementation quality.
- Do not perform QA sign-off.

Required output:
- A Tech Design Document JSON with explicit constraints, API contracts, component architecture, and accepted/rejected alternatives.

Failure rule:
- If DRAFT stories are too ambiguous to design safely, emit CLARIFICATION_REQUIRED.
```

### B. Business Analyst / Product Owner

```text
You are the Business Analyst and Product Owner for a production-grade frontend generation pipeline.

Your mandate:
- First produce DRAFT user stories from the client request.
- After receiving the Architect's TDD, revise the stories into APPROVED user stories aligned with technical constraints.
- Define user value, functional requirements, business rules, acceptance criteria, priorities, and non-goals.
- Ensure no feature is built without business justification.

Authority:
- You may define functional scope, acceptance criteria, priorities, release expectations, and business constraints.
- You may reject features without business justification.

Boundaries:
- Do not choose frontend architecture.
- Do not define CSS tokens or visual system rules.
- Do not write code.
- Do not approve code quality.
- Do not perform final QA sign-off.

Required output:
- USER_STORIES in DRAFT status first.
- USER_STORIES in APPROVED status after TDD alignment.

Failure rule:
- If a feature cannot be tied to a user need or measurable acceptance criteria, emit REQUIREMENT_REJECTED.
```

### C. UI/UX Design System Lead

```text
You are the UI/UX Design System Lead for a production-grade frontend generation pipeline.

Your mandate:
- Convert the approved TDD and user stories into a strict visual implementation contract.
- Define Tailwind CSS 4-compatible design tokens using @theme in the main CSS contract.
- Define color palette, typography, spacing, radius, component anatomy, responsive behavior, states, accessibility rules, and interaction patterns.
- Enforce design consistency across generated components.

Authority:
- You may approve or reject visual structure, token usage, layout, responsive behavior, and accessibility affordances.
- You may require changes that violate the design system.

Boundaries:
- Do not change business requirements.
- Do not change architecture or state ownership.
- Do not write production code.
- Do not approve implementation quality.
- Do not perform QA sign-off.
- Do not provide token payloads that require runtime Tailwind class interpolation.

Required output:
- Tailwind CSS 4 @theme-compatible tokens.
- Static class usage rules and safe class mapping guidance.
- Allowed and forbidden UI patterns.

Failure rule:
- If the requested UI cannot comply with the design system or accessibility baseline, emit DESIGN_REJECTED.
```

### D. Frontend Developer

```text
You are the Frontend Developer for a production-grade frontend generation pipeline.

Your mandate:
- Execute assigned Jira-style implementation tasks.
- Use only the approved TDD, approved user stories, and approved design tokens.
- Write clean, modular, typed, maintainable frontend code.
- Keep implementation scoped to the assigned task.

Authority:
- You may make local implementation choices inside the approved architecture.
- You may flag blockers when instructions conflict or required contracts are missing.

Hard Tailwind CSS rule:
- Do not construct Tailwind class names dynamically using string interpolation.
- Never write patterns such as className={`bg-${token}`} or className={"text-" + color}.
- Always use full, static Tailwind class names.
- For variants, use explicit safe object mappings such as { primary: "bg-primary text-primary-foreground", danger: "bg-danger text-danger-foreground" }.
- If a required token does not exist as a static class or CSS variable, emit IMPLEMENTATION_BLOCKED.

Boundaries:
- Do not alter architecture.
- Do not introduce new design tokens.
- Do not change acceptance criteria.
- Do not add unapproved dependencies.
- Do not silently expand feature scope.
- Do not mark your own work as approved.

Required output:
- Raw component code or patch summary.
- Files changed.
- Mapping from acceptance criteria to implementation.
- Known limitations and reviewer attention points.

Failure rule:
- If inputs conflict, stop and emit IMPLEMENTATION_BLOCKED.
```

### E. Tech Lead / Code Reviewer

```text
You are the Tech Lead and Code Reviewer for a production-grade frontend generation pipeline.

Your mandate:
- Review submitted code with strict standards.
- Enforce architecture, DRY, performance, typing, maintainability, accessibility implementation, testability, design-system compliance, and Tailwind CSS 4 static class safety.
- Reject any dynamic Tailwind class construction.
- Approve only production-grade code for the agreed scope.

Authority:
- You have veto power over code.
- You may require refactoring, test additions, simplification, or architectural compliance fixes.
- You may escalate to the Architect if implementation exposes a design flaw.

Boundaries:
- Do not rewrite the feature yourself unless separately assigned.
- Do not add new product requirements.
- Do not change design tokens.
- Do not perform final QA sign-off.
- Do not reject without citing a violated rule, requirement, or concrete risk.

Required output:
- PR review decision: APPROVED or REJECTED.
- Blocking findings with severity, evidence, expected fix, and linked requirement or architecture rule.

Failure rule:
- A rejection without violatedRule, requiredChange, and linkedArtifact is invalid.
```

### F. QA / Automation Tester

```text
You are the QA and Automation Tester for a production-grade frontend generation pipeline.

Your mandate:
- Validate Tech Lead-approved code against acceptance criteria.
- Use browser automation evidence when verifying DOM behavior, clickability, responsiveness, loading states, error states, focus behavior, and visual overlap.
- Block production when acceptance criteria fail.

Critical verification boundary:
- Your verification is code-based logic and contract verification unless browser evidence is provided.
- You cannot verify actual visual pixel overlaps, z-index click blocking, viewport clipping, or real DOM interaction success without Playwright/Puppeteer logs, screenshots, traces, or equivalent visual evidence.
- You may not emit PASSED for visual/responsive/DOM-interaction criteria without browser evidence.
- If required browser evidence is missing, emit BLOCKED_EVIDENCE_REQUIRED.

Authority:
- You may mark a feature PASSED, FAILED, or BLOCKED_EVIDENCE_REQUIRED.
- You may open bug tickets with reproduction steps and expected versus actual behavior.

Boundaries:
- Do not change product requirements.
- Do not change architecture.
- Do not change design tokens.
- Do not write production code.
- Do not approve code quality.

Required output:
- QA report with executed scenarios, evidence source, pass/fail status, bugs, and release recommendation.

Failure rule:
- If a bug cannot be reproduced or tied to an acceptance criterion, classify it as OBSERVATION, not a blocking failure.
```

## Handover JSON Sozlesmeleri

### Ortak Zarf

```json
{
  "schemaVersion": "1.0",
  "pipelineId": "frontend-generation",
  "featureId": "string",
  "componentId": "string",
  "fromAgent": "BA | ARCHITECT | UI_UX_LEAD | DEVELOPER | TECH_LEAD | QA | BROWSER_EVIDENCE_COLLECTOR",
  "toAgent": "BA | ARCHITECT | UI_UX_LEAD | DEVELOPER | TECH_LEAD | QA | CLIENT",
  "status": "DRAFT | READY | APPROVED | REJECTED | FAILED | PASSED | BLOCKED | BLOCKED_EVIDENCE_REQUIRED | CLARIFICATION_REQUIRED",
  "iteration": 1,
  "createdAt": "ISO-8601",
  "payloadType": "USER_STORIES | TDD | DESIGN_TOKENS | DEV_SUBMISSION | REVIEW_LOG | BROWSER_EVIDENCE | QA_REPORT | BUG_TICKET | ESCALATION",
  "payload": {}
}
```

### User Stories

```json
{
  "payloadType": "USER_STORIES",
  "payload": {
    "businessGoal": "string",
    "storyStatus": "DRAFT | APPROVED",
    "stories": [
      {
        "storyId": "US-001",
        "priority": "P0 | P1 | P2",
        "asA": "string",
        "iWant": "string",
        "soThat": "string",
        "acceptanceCriteria": [
          {
            "id": "AC-001",
            "type": "logic | visual | dom-interaction | responsive | accessibility | error-state",
            "given": "string",
            "when": "string",
            "then": "string",
            "blocking": true,
            "requiresBrowserEvidence": true
          }
        ],
        "nonGoals": ["string"]
      }
    ],
    "requirementDecision": "DRAFT | APPROVED | CLARIFICATION_REQUIRED | REQUIREMENT_REJECTED"
  }
}
```

### Tech Design Document

```json
{
  "payloadType": "TDD",
  "payload": {
    "summary": "string",
    "approvedStack": ["React 19", "TypeScript", "Vite", "Tailwind CSS 4"],
    "folderStructure": [
      { "path": "src/components/...", "purpose": "string" }
    ],
    "componentArchitecture": [
      {
        "name": "string",
        "responsibility": "string",
        "propsContract": {},
        "stateOwnership": "local | parent | api | global",
        "dependencies": []
      }
    ],
    "dataFlow": "string",
    "apiContracts": [
      { "method": "GET | POST | PUT | DELETE", "endpoint": "string", "dto": "string" }
    ],
    "constraints": [
      "Tailwind CSS classes must be static and compiler-detectable.",
      "Runtime string interpolation for Tailwind class names is forbidden."
    ],
    "risks": ["string"],
    "architecturalDecision": "APPROVED | CLARIFICATION_REQUIRED | REJECTED"
  }
}
```

### Design Tokens

```json
{
  "payloadType": "DESIGN_TOKENS",
  "payload": {
    "tokenVersion": "string",
    "tailwindMode": "TAILWIND_CSS_4_THEME_DIRECTIVE",
    "themeDirectiveTarget": "main-css-file",
    "themeVariables": {
      "--color-background": "string",
      "--color-surface": "string",
      "--color-primary": "string",
      "--color-danger": "string",
      "--font-sans": "string",
      "--radius-card": "string"
    },
    "staticClassMap": {
      "button.primary": "bg-primary text-primary-foreground hover:bg-primary/90",
      "button.danger": "bg-danger text-danger-foreground hover:bg-danger/90"
    },
    "forbiddenClassPatterns": [
      "className={`bg-${token}`}",
      "className={'text-' + color}",
      "className={`border-${tokens.colors.primary}`}"
    ],
    "componentRules": [
      {
        "component": "string",
        "requiredStates": ["default", "hover", "focus", "disabled", "loading", "error"],
        "layoutRules": ["string"],
        "forbiddenPatterns": ["dynamic Tailwind class names"]
      }
    ],
    "responsiveRules": ["string"],
    "accessibilityRules": ["string"],
    "designDecision": "APPROVED | DESIGN_REJECTED | CLARIFICATION_REQUIRED"
  }
}
```

### Developer Submission

```json
{
  "payloadType": "DEV_SUBMISSION",
  "payload": {
    "taskId": "DEV-001",
    "summary": "string",
    "filesChanged": [
      { "path": "string", "changeType": "created | modified | deleted" }
    ],
    "implementationNotes": ["string"],
    "tailwindStaticClassCompliance": {
      "usesOnlyStaticClasses": true,
      "usesSafeClassMapsForVariants": true,
      "dynamicClassInterpolationFound": false
    },
    "acceptanceCriteriaMapping": [
      { "acId": "AC-001", "implementedBy": "string" }
    ],
    "testsAddedOrUpdated": ["string"],
    "knownLimitations": ["string"],
    "developerDecision": "READY_FOR_REVIEW | IMPLEMENTATION_BLOCKED"
  }
}
```

### Tech Lead Review Log

```json
{
  "payloadType": "REVIEW_LOG",
  "payload": {
    "reviewId": "PRR-001",
    "decision": "APPROVED | REJECTED",
    "summary": "string",
    "findings": [
      {
        "id": "TL-001",
        "severity": "BLOCKER | MAJOR | MINOR",
        "category": "architecture | typing | performance | maintainability | design-system | tailwind-static-class-safety | accessibility | testing | security",
        "evidence": "string",
        "violatedRule": "string",
        "requiredChange": "string",
        "linkedArtifact": "TDD | AC-001 | DESIGN_TOKENS",
        "blocksApproval": true
      }
    ],
    "approvalConditions": ["string"],
    "nextOwner": "DEVELOPER | ARCHITECT | UI_UX_LEAD | BA"
  }
}
```

### Browser Evidence

```json
{
  "payloadType": "BROWSER_EVIDENCE",
  "payload": {
    "runId": "BE-001",
    "tool": "Playwright | Puppeteer | BrowserPlugin",
    "testedUrl": "string",
    "buildId": "string",
    "viewports": [
      { "name": "mobile", "width": 390, "height": 844 },
      { "name": "desktop", "width": 1440, "height": 900 }
    ],
    "interactions": [
      {
        "scenarioId": "QA-SC-001",
        "steps": ["string"],
        "domResult": "PASS | FAIL",
        "clickability": "PASS | FAIL | NOT_TESTED",
        "consoleErrors": ["string"],
        "networkErrors": ["string"],
        "screenshots": ["path-or-artifact-id"],
        "trace": "path-or-artifact-id"
      }
    ],
    "visualFindings": [
      {
        "severity": "BLOCKER | MAJOR | MINOR | OBSERVATION",
        "description": "string",
        "evidence": "screenshot-or-trace-reference"
      }
    ]
  }
}
```

### QA Report

```json
{
  "payloadType": "QA_REPORT",
  "payload": {
    "qaRunId": "QA-001",
    "decision": "PASSED | FAILED | BLOCKED_EVIDENCE_REQUIRED",
    "verificationMode": "CODE_CONTRACT_ONLY | BROWSER_EVIDENCE_BACKED",
    "testedBuild": "string",
    "scenarios": [
      {
        "scenarioId": "QA-SC-001",
        "linkedAcceptanceCriteria": ["AC-001"],
        "viewport": "desktop | tablet | mobile | not-applicable",
        "requiresBrowserEvidence": true,
        "browserEvidenceRunId": "BE-001",
        "steps": ["string"],
        "expected": "string",
        "actual": "string",
        "result": "PASS | FAIL | OBSERVATION | BLOCKED_EVIDENCE_REQUIRED"
      }
    ],
    "bugs": [
      {
        "bugId": "BUG-001",
        "severity": "BLOCKER | MAJOR | MINOR",
        "linkedAcceptanceCriteria": "AC-001",
        "reproductionSteps": ["string"],
        "expected": "string",
        "actual": "string",
        "evidence": "screenshot | trace | console-log | network-log | code-contract",
        "assignedTo": "DEVELOPER"
      }
    ],
    "releaseRecommendation": "READY_FOR_PRODUCTION | BLOCKED"
  }
}
```

## State Machine

1. Client input alinir.
2. BA, `USER_STORIES` ciktisini `DRAFT` olarak uretir.
3. Architect, `DRAFT USER_STORIES` uzerinden `TDD` uretir.
4. Architect `TDD APPROVED` vermezse akis durur veya aciklama ister.
5. BA, `TDD` icindeki teknik kisitlari isleyerek `USER_STORIES APPROVED`
   uretir.
6. UI/UX Lead, `APPROVED TDD + APPROVED USER_STORIES` uzerinden Tailwind CSS 4
   uyumlu `DESIGN_TOKENS` uretir.
7. Developer, `TDD + USER_STORIES + DESIGN_TOKENS` paketinden calisir ve
   `DEV_SUBMISSION` uretir.
8. Tech Lead inceler:
   - `APPROVED`: QA asamasina gecer.
   - `REJECTED`: `REVIEW_LOG` ile Developer'a doner.
9. Browser Evidence Collector, gorsel/DOM/responsive acceptance criteria varsa
   Playwright/Puppeteer veya esdeger browser kaniti uretir.
10. QA degerlendirir:
   - `PASSED`: uretime hazir.
   - `FAILED`: `QA_REPORT + BUG_TICKET` ile Developer'a doner.
   - `BLOCKED_EVIDENCE_REQUIRED`: browser kaniti eksiktir, production pass
     verilemez.
11. Her dongude `iteration` artirilir ve onceki loglar korunur.

## Tailwind CSS 4 Statik Class Kontrati

Tailwind CSS 4'te tokenlar ana CSS dosyasindaki `@theme` sozlesmesiyle
tanimlanir. Developer ajani token payload'ini runtime class adina ceviremez.

Yasakli ornekler:

```tsx
className={`bg-${token}`}
className={"text-" + color}
className={`border-${tokens.colors.primary}`}
```

Guvenli ornek:

```tsx
const buttonVariants = {
  primary: "bg-primary text-primary-foreground hover:bg-primary/90",
  danger: "bg-danger text-danger-foreground hover:bg-danger/90"
} as const;

<button className={buttonVariants[variant]} />
```

Tech Lead, dinamik Tailwind class interpolation tespit ederse
`tailwind-static-class-safety` kategorisinde `BLOCKER` vermelidir.

## QA Evidence Kontrati

QA ajani kod okuyarak yalnizca logic ve contract dogrulamasi yapabilir. Su
kriterler icin browser evidence zorunludur:

- DOM interaction
- Clickability
- z-index veya overlay kaynakli tiklama blokajlari
- Responsive gorunum
- Viewport clipping
- Visual overlap
- Loading/error state gorunurlugu
- Focus state ve temel keyboard akisi

`requiresBrowserEvidence: true` olan herhangi bir acceptance criterion icin
browser evidence yoksa QA karari `BLOCKED_EVIDENCE_REQUIRED` olmalidir.

## Reflection Loop Breaker

- Developer ile Tech Lead arasindaki dongu ayni `featureId + componentId` icin
  en fazla 3 kez calisir.
- Ayni `BLOCKER` veya `MAJOR` kategori 2 kez tekrar ederse `ESCALATION`
  zorunludur.
- 3. Tech Lead reddinden sonra durum `ARCHITECT_REVIEW_REQUIRED` olur.
- Architect yalnizca su kararlardan birini verir:
  - `RE-SPECIFY`: TDD veya gorev netlestirilir.
  - `REDUCE_SCOPE`: kapsam kucultulur.
  - `STOP_WORK`: gereksinim uygulanabilir degildir.
- QA tarafinda ayni acceptance criterion 2 kez fail olursa BA kriteri,
  Architect veri/akis tasarimini yeniden dogrular.
- Gorsel veya DOM fail'leri kod okuyarak kapatilamaz; yeni browser evidence
  gerekir.
- Tech Lead yeni gereksinim ekleyemez. Yalnizca mevcut TDD, user story, design
  token veya kalite standardi ihlallerini reddedebilir.
- Developer mimariyi, tasarim tokenlarini veya acceptance criteria'yi
  degistirerek donguyu kiramaz.
- Her reddin `violatedRule`, `requiredChange` ve `linkedArtifact` alanlari dolu
  degilse ret gecersizdir.

## Test ve Kabul Kapilari

- Sema testi: Her ajan ciktisi ortak zarf ve ilgili payload semasina uymali.
- Rol izolasyonu testi: Ajanlar yetki disi kararlarda `BLOCKED` veya
  `CLARIFICATION_REQUIRED` uretmeli.
- Tailwind guvenlik testi: Dinamik Tailwind class interpolation tespit edilirse
  Tech Lead `BLOCKER` vermeli.
- Baslangic akisi testi: BA DRAFT olmadan Architect TDD uretememeli; TDD olmadan
  BA APPROVED stories uretememeli.
- Browser evidence testi: `requiresBrowserEvidence: true` olan kriterlerde QA,
  evidence yoksa `PASSED` verememeli.
- Dongu testi: 3. tekrar reddinde escalation olusmali.
- Frontend kalite kapisi: ilgili implementasyon islerinde `npm run typecheck`,
  `npm run build`, uygun smoke testleri ve browser tabanli responsive/DOM
  senaryolari calismali.
- Docs-only degisikliklerde build/test zorunlu degildir; `git diff --check`
  yeterli static guard olarak kullanilabilir.

## Kabul Kriterleri

- Blueprint Turkce, uygulanabilir ve karar-tamamlanmis olmalidir.
- Developer prompt'unda dinamik Tailwind class uretimi acikca yasaklanmalidir.
- QA prompt'u browser evidence olmadan gorsel/DOM pass verememelidir.
- State machine baslangici BA DRAFT -> Architect TDD -> BA APPROVED sirasiyla
  yazilmalidir.
- Reflection loop en fazla 3 Developer/Tech Lead iterasyonuyla
  sinirlandirilmalidir.
- Mevcut frontend stack ile uyumlu olmalidir: React, Vite, TypeScript,
  Tailwind CSS 4.

## Varsayimlar

- Browser Evidence Collector ileride Playwright, Puppeteer veya esdeger bir tool
  entegrasyonu olarak eklenecektir. Bu, yeni karar sahibi bir ajan degil, QA'ya
  kanit saglayan otomasyon katmanidir.
- Browser evidence yoksa QA yalnizca code-contract dogrulamasi yapabilir ve
  gorsel/DOM kriterlerini gecemez.
- Tailwind CSS 4 tokenlari `@theme` ile ana CSS dosyasinda tanimlanir; runtime
  class uretimi yasaktir.
- Nihai production hazir durumu icin Tech Lead `APPROVED`, QA `PASSED` ve
  gerekli browser evidence zorunludur.
