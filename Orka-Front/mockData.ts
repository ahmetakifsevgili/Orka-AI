import type { Topic, QuizData, WikiContent, AIResponse } from "./types";

// ─── Initial Topics ───────────────────────────────────────────────
export const initialTopics: Topic[] = [
  {
    id: "py",
    title: "Python Fundamentals",
    icon: "🐍",
    subLessons: [
      { id: "py-1", title: "Variables & Data Types", completed: true },
      { id: "py-2", title: "Control Flow", completed: true },
      { id: "py-3", title: "Functions", completed: false },
      { id: "py-4", title: "Object-Oriented Programming", completed: false },
    ],
    createdAt: new Date("2024-01-15"),
  },
  {
    id: "ml",
    title: "Machine Learning",
    icon: "🧠",
    subLessons: [
      { id: "ml-1", title: "Supervised Learning", completed: true },
      { id: "ml-2", title: "Neural Networks", completed: false },
      { id: "ml-3", title: "Model Evaluation", completed: false },
    ],
    createdAt: new Date("2024-02-01"),
  },
];

// ─── Quiz Pool ────────────────────────────────────────────────────
const quizPool: QuizData[] = [
  {
    topic: "Python Fundamentals",
    question: "What is the output of `print(type([]))` in Python?",
    options: [
      { id: "q1-a", text: "<class 'tuple'>", isCorrect: false },
      { id: "q1-b", text: "<class 'list'>", isCorrect: true },
      { id: "q1-c", text: "<class 'dict'>", isCorrect: false },
      { id: "q1-d", text: "<class 'set'>", isCorrect: false },
    ],
    explanation:
      "An empty `[]` creates a list object in Python. The `type()` function returns `<class 'list'>` for any list, including empty ones.",
  },
  {
    topic: "C# Generics",
    question: "In C#, what is the primary benefit of using Generics?",
    options: [
      { id: "q2-a", text: "They make code run faster at compile time", isCorrect: false },
      { id: "q2-b", text: "They allow type-safe data structures without boxing/unboxing", isCorrect: true },
      { id: "q2-c", text: "They replace the need for interfaces", isCorrect: false },
      { id: "q2-d", text: "They enable multiple inheritance", isCorrect: false },
    ],
    explanation:
      "Generics provide type safety at compile time and eliminate the need for boxing/unboxing when working with value types, improving both safety and performance.",
  },
  {
    topic: "Algorithms & Data Structures",
    question: "What is the time complexity of binary search?",
    options: [
      { id: "q3-a", text: "O(n)", isCorrect: false },
      { id: "q3-b", text: "O(n log n)", isCorrect: false },
      { id: "q3-c", text: "O(log n)", isCorrect: true },
      { id: "q3-d", text: "O(1)", isCorrect: false },
    ],
    explanation:
      "Binary search divides the search space in half with each comparison, resulting in O(log n) time complexity. This requires the input array to be sorted.",
  },
  {
    topic: "Introduction to Machine Learning",
    question: "Which of the following is a supervised learning algorithm?",
    options: [
      { id: "q4-a", text: "K-Means Clustering", isCorrect: false },
      { id: "q4-b", text: "Principal Component Analysis", isCorrect: false },
      { id: "q4-c", text: "Random Forest", isCorrect: true },
      { id: "q4-d", text: "DBSCAN", isCorrect: false },
    ],
    explanation:
      "Random Forest is a supervised learning algorithm that uses an ensemble of decision trees for classification and regression. K-Means, PCA, and DBSCAN are all unsupervised methods.",
  },
];

// ─── Wiki Content ─────────────────────────────────────────────────
export const wikiContents: Record<string, WikiContent> = {
  "py-1": {
    topicId: "py",
    subLessonId: "py-1",
    title: "Variables & Data Types",
    content: `## Variables & Data Types in Python

Python is a **dynamically typed** language, meaning you don't need to declare variable types explicitly. The interpreter infers the type at runtime.

### Variable Assignment

\`\`\`python
# Simple assignments
name = "Alice"          # str
age = 30                # int
height = 5.7            # float
is_student = True       # bool
\`\`\`

### Core Data Types

| Type | Example | Mutable | Description |
|------|---------|---------|-------------|
| \`int\` | \`42\` | No | Whole numbers |
| \`float\` | \`3.14\` | No | Decimal numbers |
| \`str\` | \`"hello"\` | No | Text sequences |
| \`bool\` | \`True\` | No | Boolean values |
| \`list\` | \`[1, 2, 3]\` | Yes | Ordered collection |
| \`tuple\` | \`(1, 2, 3)\` | No | Immutable sequence |
| \`dict\` | \`{"a": 1}\` | Yes | Key-value mapping |
| \`set\` | \`{1, 2, 3}\` | Yes | Unique elements |

### Type Conversion

\`\`\`python
# Explicit type conversion
x = int("42")       # str → int
y = float(42)       # int → float
z = str(42)          # int → str
w = list("hello")   # str → list: ['h', 'e', 'l', 'l', 'o']
\`\`\`

### Type Checking

\`\`\`python
x = 42
print(type(x))           # <class 'int'>
print(isinstance(x, int)) # True
\`\`\``,
    keyPoints: [
      "Python uses dynamic typing — no explicit type declarations needed",
      "Core types: int, float, str, bool, list, tuple, dict, set",
      "Use type() to check types, isinstance() for type validation",
      "Mutable types (list, dict, set) can be modified in place",
    ],
    lastUpdated: new Date("2024-01-20"),
  },
  "ml-1": {
    topicId: "ml",
    subLessonId: "ml-1",
    title: "Supervised Learning",
    content: `## Supervised Learning

Supervised learning is a machine learning paradigm where the model learns from **labeled training data** — each input has a corresponding known output.

### How It Works

1. **Training Phase**: The model receives input-output pairs \`(X, y)\`
2. **Learning**: It finds patterns mapping inputs to outputs
3. **Prediction**: Given new input \`X_new\`, it predicts \`y_new\`

### Algorithm Comparison

| Algorithm | Type | Best For | Complexity |
|-----------|------|----------|------------|
| Linear Regression | Regression | Continuous values | Low |
| Logistic Regression | Classification | Binary outcomes | Low |
| Decision Tree | Both | Interpretable models | Medium |
| Random Forest | Both | High accuracy | High |
| SVM | Both | Small datasets | High |
| Neural Network | Both | Complex patterns | Very High |

### Example: Linear Regression

\`\`\`python
from sklearn.linear_model import LinearRegression
from sklearn.model_selection import train_test_split

# Split data
X_train, X_test, y_train, y_test = train_test_split(
    X, y, test_size=0.2, random_state=42
)

# Train model
model = LinearRegression()
model.fit(X_train, y_train)

# Evaluate
score = model.score(X_test, y_test)
print(f"R² Score: {score:.4f}")
\`\`\`

### Key Concepts

- **Overfitting**: Model memorizes training data, fails on new data
- **Underfitting**: Model is too simple to capture patterns
- **Bias-Variance Tradeoff**: Balance between model complexity and generalization`,
    keyPoints: [
      "Supervised learning uses labeled data (input-output pairs)",
      "Two main types: Classification (discrete) and Regression (continuous)",
      "Key algorithms: Linear/Logistic Regression, Decision Trees, Random Forest, SVM",
      "Always split data into training and test sets to evaluate generalization",
    ],
    lastUpdated: new Date("2024-02-05"),
  },
  "csg-1": {
    topicId: "cs",
    subLessonId: "csg-1",
    title: "C# Generics Introduction",
    content: `## C# Generics

Generics allow you to write **type-safe, reusable code** without sacrificing performance. They eliminate the need for boxing/unboxing and runtime type checks.

### Before Generics (The Problem)

\`\`\`csharp
// Without generics — uses object, requires casting
ArrayList list = new ArrayList();
list.Add(42);           // Boxing: int → object
int value = (int)list[0]; // Unboxing + casting
list.Add("oops");       // No compile-time error!
\`\`\`

### After Generics (The Solution)

\`\`\`csharp
// With generics — type-safe at compile time
List<int> list = new List<int>();
list.Add(42);           // No boxing
int value = list[0];    // No casting needed
// list.Add("oops");    // Compile error!
\`\`\`

### Creating Generic Classes

\`\`\`csharp
public class Repository<T> where T : class
{
    private readonly List<T> _items = new();

    public void Add(T item) => _items.Add(item);
    public T? GetById(int index) =>
        index < _items.Count ? _items[index] : default;
    public IEnumerable<T> GetAll() => _items.AsReadOnly();
}

// Usage
var userRepo = new Repository<User>();
userRepo.Add(new User("Alice"));
\`\`\`

### Generic Constraints

| Constraint | Description |
|-----------|-------------|
| \`where T : class\` | T must be a reference type |
| \`where T : struct\` | T must be a value type |
| \`where T : new()\` | T must have parameterless constructor |
| \`where T : IComparable\` | T must implement interface |
| \`where T : Base\` | T must derive from Base class |`,
    keyPoints: [
      "Generics provide compile-time type safety",
      "They eliminate boxing/unboxing overhead for value types",
      "Use constraints (where T : ...) to restrict type parameters",
      "Common generic types: List<T>, Dictionary<TKey, TValue>, Task<T>",
    ],
    lastUpdated: new Date("2024-02-10"),
  },
};

// ─── Quick Prompts ────────────────────────────────────────────────
export const QUICK_PROMPTS = [
  { icon: "🐍", label: "Python Basics", prompt: "I want to learn Python fundamentals" },
  { icon: "🧠", label: "Machine Learning", prompt: "Teach me about machine learning" },
  { icon: "⚡", label: "C# Generics", prompt: "I want to learn C# Generics /plan" },
  { icon: "💻", label: "Web Development", prompt: "I want to learn web development /plan" },
];

// ─── Thinking States ──────────────────────────────────────────────
export const THINKING_STATES = [
  "Searching knowledge base...",
  "Planning curriculum...",
  "Analyzing your learning style...",
  "Generating response...",
];

// ─── AI Response Engine ───────────────────────────────────────────
let messageCount = 0;

const greetings = ["hello", "hi", "hey", "merhaba", "selam", "yo", "sup"];

const educationalResponses: string[] = [
  `## Understanding the Concept

Great question! Let me break this down for you in a structured way.

### Key Points

- **Foundation**: Every complex concept builds on simpler primitives. Understanding the basics deeply is more valuable than surface-level knowledge of advanced topics.
- **Practice**: Theory without practice is incomplete. Try implementing what you learn in small projects.
- **Iteration**: Learning is not linear — revisit concepts as your understanding deepens.

### Practical Example

\`\`\`python
# Here's a simple demonstration
def learn(concept, depth=1):
    """Recursive learning — each iteration deepens understanding"""
    understanding = study(concept)
    if depth < mastery_level:
        return learn(concept, depth + 1)
    return understanding
\`\`\`

### Next Steps

1. Review the fundamentals we covered
2. Try the practice exercises below
3. Use \`/plan\` to create a structured learning path

> "The expert in anything was once a beginner." — Helen Hayes`,

  `## Deep Dive Analysis

Let me provide a comprehensive explanation of this topic.

### Core Principles

Understanding this requires grasping three fundamental ideas:

1. **Abstraction** — Hiding complexity behind simple interfaces
2. **Composition** — Building complex systems from simple parts
3. **Separation of Concerns** — Each module handles one responsibility

### Implementation Pattern

\`\`\`python
class LearningModule:
    def __init__(self, topic: str):
        self.topic = topic
        self.progress = 0.0
        self.notes = []

    def study(self, material: str) -> float:
        """Process learning material and update progress"""
        comprehension = self._analyze(material)
        self.progress += comprehension * 0.1
        return min(self.progress, 1.0)

    def _analyze(self, material: str) -> float:
        # Simulated comprehension scoring
        return len(material.split()) / 100
\`\`\`

### Comparison Table

| Approach | Pros | Cons | Use When |
|----------|------|------|----------|
| Top-Down | Big picture first | Can miss details | New domain |
| Bottom-Up | Strong foundations | Slow start | Technical depth |
| Hybrid | Balanced | Requires planning | Most cases |

### Summary

The key takeaway is that effective learning combines theory with practice. Don't just read — **build something** with what you learn.`,

  `## Comprehensive Guide

This is a fundamental topic that every developer should master. Let me walk you through it step by step.

### Prerequisites

Before diving in, make sure you understand:
- Basic programming concepts (variables, functions, loops)
- Data structures fundamentals
- How to read documentation effectively

### The Mental Model

Think of this concept like building with LEGO blocks:

1. **Individual blocks** = primitive operations
2. **Sub-assemblies** = functions and modules
3. **Complete model** = working application

### Code Walkthrough

\`\`\`python
from typing import List, Optional

def binary_search(arr: List[int], target: int) -> Optional[int]:
    """
    Classic binary search implementation.
    Returns the index of target, or None if not found.

    Time: O(log n) | Space: O(1)
    """
    left, right = 0, len(arr) - 1

    while left <= right:
        mid = (left + right) // 2
        if arr[mid] == target:
            return mid
        elif arr[mid] < target:
            left = mid + 1
        else:
            right = mid - 1

    return None

# Usage
sorted_data = [1, 3, 5, 7, 9, 11, 13, 15]
result = binary_search(sorted_data, 7)
print(f"Found at index: {result}")  # Output: Found at index: 3
\`\`\`

### Key Insights

- **Efficiency matters** at scale — O(log n) vs O(n) is the difference between milliseconds and minutes on large datasets
- **Sorted input** is a prerequisite — always verify your assumptions
- **Edge cases** to consider: empty array, single element, target not present`,
];

function generatePlanContent(topicName: string): string {
  return `## Learning Plan: ${topicName}

I've created a structured curriculum for **${topicName}**. This plan is designed to take you from fundamentals to real-world application.

### Curriculum Overview

| Phase | Focus Area | Duration | Difficulty |
|-------|-----------|----------|------------|
| 1 | Foundations | 1-2 weeks | Beginner |
| 2 | Intermediate Concepts | 2-3 weeks | Intermediate |
| 3 | Advanced Topics | 2-3 weeks | Advanced |
| 4 | Real-World Applications | 1-2 weeks | Applied |

### Phase 1: Foundations
Start with the core building blocks. This phase ensures you have a solid base before moving to complex topics.

### Phase 2: Intermediate Concepts
Build on your foundations with more nuanced patterns and techniques. This is where most of the "aha moments" happen.

### Phase 3: Advanced Topics
Dive deep into sophisticated concepts that separate beginners from experts.

### Phase 4: Real-World Applications
Apply everything you've learned to practical, production-quality projects.

---

> Your learning plan has been added to the sidebar. Click on any sub-lesson to view its wiki content and take notes.`;
}

function createTopicFromPlan(topicName: string): Topic {
  const id = topicName.toLowerCase().replace(/\s+/g, "-").slice(0, 10);
  return {
    id,
    title: topicName,
    icon: getTopicIcon(topicName),
    subLessons: [
      { id: `${id}-1`, title: "Foundations", completed: false },
      { id: `${id}-2`, title: "Intermediate Concepts", completed: false },
      { id: `${id}-3`, title: "Advanced Topics", completed: false },
      { id: `${id}-4`, title: "Real-World Applications", completed: false },
    ],
    createdAt: new Date(),
  };
}

function getTopicIcon(name: string): string {
  const lower = name.toLowerCase();
  if (lower.includes("python")) return "🐍";
  if (lower.includes("machine") || lower.includes("ml")) return "🧠";
  if (lower.includes("c#") || lower.includes("csharp")) return "⚡";
  if (lower.includes("web")) return "🌐";
  if (lower.includes("react")) return "⚛️";
  if (lower.includes("data")) return "📊";
  if (lower.includes("algorithm")) return "🔍";
  if (lower.includes("javascript") || lower.includes("js")) return "📜";
  return "📚";
}

export function generateAIResponse(
  userMessage: string,
  _topics: Topic[]
): AIResponse {
  const msg = userMessage.trim().toLowerCase();

  // Rule 1: Small Talk Guard
  if (msg.length < 4 || greetings.some((g) => msg === g || msg.startsWith(g + " "))) {
    return {
      content: `## Welcome to Orka AI! 👋

I'm your intelligent learning companion. I help you master technical subjects through structured curricula, interactive quizzes, and a living knowledge wiki.

### How to get started:

- **Ask me anything** — I'll provide detailed, educational explanations
- **Use \`/plan\`** — Create a structured learning path (e.g., "I want to learn React Hooks /plan")
- **Click topics** in the sidebar to explore your knowledge wiki

What would you like to learn today?`,
      type: "text",
    };
  }

  // Rule 2: /plan Trigger
  if (msg.includes("/plan")) {
    const topicName = userMessage
      .replace(/\/plan/gi, "")
      .replace(/i want to learn|teach me about|learn/gi, "")
      .trim() || "General Programming";

    const capitalizedName = topicName
      .split(" ")
      .map((w) => w.charAt(0).toUpperCase() + w.slice(1))
      .join(" ");

    const newTopic = createTopicFromPlan(capitalizedName);

    return {
      content: generatePlanContent(capitalizedName),
      type: "plan",
      newTopic,
    };
  }

  // Rule 3: Quiz Trigger (every 2nd-3rd message)
  messageCount++;
  if (messageCount > 1 && Math.random() > 0.55) {
    const quiz = quizPool[Math.floor(Math.random() * quizPool.length)];
    return {
      content: `## Quick Knowledge Check

Let's test your understanding with a quick quiz. This helps reinforce what you've learned and identifies areas for review.`,
      type: "quiz",
      quiz,
    };
  }

  // Rule 4: Default Educational Response
  const response = educationalResponses[Math.floor(Math.random() * educationalResponses.length)];
  return {
    content: response,
    type: "plan",
  };
}
