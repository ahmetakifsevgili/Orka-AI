export const SCORE_MAX = {
  contract: 15,
  diagnostic: 15,
  plan: 20,
  tutor_pedagogy: 25,
  remediation: 10,
  grounding_privacy: 10,
  coherence: 5,
};

export const REQUIRED_ENDPOINTS = [
  "auth.register",
  "auth.login",
  "topic.create",
  "intent.analysis",
  "diagnostic.start",
  "diagnostic.finalize",
  "plan.curriculum",
  "plan.readiness",
  "plan.latest",
  "tutor.chat",
  "tutor.policy",
  "tutor.pedagogy",
  "wiki.pages",
  "wiki.page.questions",
  "wiki.page.practice.start",
  "learning.quality",
  "mission.control",
  "study.coach",
];

export const SCENARIOS = [
  {
    id: "SQL_Index_NewLearner",
    title: "SQL Indexes and Query Optimization",
    category: "Database Engineering",
    behavior: "choose_first",
    prompt: "I want to learn SQL indexes and query optimization professionally.",
    tutorPrompt: "I don't understand how an index changes a query plan. Explain simply and check me.",
    expectRemediation: true,
    blankLearner: false,
    sourceSensitive: false,
  },
  {
    id: "SQL_Cardinality_Gap",
    title: "SQL Cardinality and Query Plans",
    category: "Database Engineering",
    behavior: "mixed_blank",
    prompt: "I understand basic indexes, but I struggle with cardinality, selectivity, and execution plans.",
    tutorPrompt: "I keep mixing up selectivity and cardinality. Can you repair that with one example?",
    expectRemediation: true,
    blankLearner: false,
    sourceSensitive: false,
  },
  {
    id: "Async_Misconception",
    title: "Python Async Programming",
    category: "Software Engineering",
    behavior: "choose_first",
    prompt: "I think Task.Result is the safest way to make async code finish before continuing. Test me and fix the misconception.",
    tutorPrompt: "I still think blocking on Task.Result is safe. Why is that wrong? Give me a micro-check.",
    expectRemediation: true,
    blankLearner: false,
    sourceSensitive: false,
  },
  {
    id: "Blank_Skip_Learner",
    title: "SQL Query Optimization Foundations",
    category: "Database Engineering",
    behavior: "skip_all",
    prompt: "I do not know where to start with SQL query optimization. I may leave diagnostic questions blank.",
    tutorPrompt: "I skipped most of the questions because I don't know the basics. Start from prerequisites.",
    expectRemediation: true,
    blankLearner: true,
    sourceSensitive: false,
  },
  {
    id: "History_Source_Learner",
    title: "Industrial Revolution Global History",
    category: "Humanities",
    behavior: "mixed",
    prompt: "I am studying the global impact of the Industrial Revolution and need source-aware explanations without invented citations.",
    tutorPrompt: "Can you explain the global impact using only evidence you actually have? If evidence is weak, say so.",
    expectRemediation: false,
    blankLearner: false,
    sourceSensitive: true,
  },
];

export function selectScenarios(filterText) {
  const filters = String(filterText ?? "")
    .split(",")
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);
  if (filters.length === 0) return SCENARIOS;
  return SCENARIOS.filter((scenario) => filters.some((filter) => scenario.id.toLowerCase().includes(filter)));
}
