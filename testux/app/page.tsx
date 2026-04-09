"use client"

import { useState, useCallback, useEffect } from "react"
import { KnowledgeSidebar } from "@/components/knowledge-sidebar"
import { ChatPanel } from "@/components/chat-panel"
import { WikiDrawer } from "@/components/wiki-drawer"
import { Header } from "@/components/header"
import { SearchDialog } from "@/components/search-dialog"
import { SettingsPanel } from "@/components/settings-panel"
import { AmbientBackground } from "@/components/ambient-background"
import type { Topic, Message, Lesson } from "@/lib/types"
import { Menu } from "lucide-react"

// Sample data for demonstration
const sampleTopics: Topic[] = [
  {
    id: "1",
    title: "C# Generics",
    expanded: true,
    lessons: [
      {
        id: "1-1",
        title: "What is a Generic?",
        completed: true,
        quizScore: 100,
        notes: `## What is a Generic?

Generics in C# allow you to define type-safe data structures without committing to actual data types. This enables you to create classes, methods, and interfaces that defer the specification of one or more types until the class or method is declared and instantiated.

### Key Benefits

- Type safety at compile time
- Code reusability
- Better performance (no boxing/unboxing)
- Cleaner, more readable code

### Basic Syntax

\`\`\`csharp
public class GenericList<T>
{
    private T[] items;
    
    public void Add(T item)
    {
        // Add item to the list
    }
}
\`\`\`

### Usage Example

- GenericList<int> for integer lists
- GenericList<string> for string lists
- GenericList<Customer> for custom types`,
      },
      {
        id: "1-2",
        title: "Generic Classes",
        completed: true,
        quizScore: 80,
        notes: `## Generic Classes

Generic classes encapsulate operations that are not specific to a particular data type. The most common use is with collection classes like List<T>, Dictionary<TKey, TValue>, etc.

### Creating a Generic Class

\`\`\`csharp
public class Stack<T>
{
    private T[] elements;
    private int count;
    
    public void Push(T item) { }
    public T Pop() { }
}
\`\`\`

### Type Constraints

You can apply constraints to limit which types can be used:

- where T : struct (value type)
- where T : class (reference type)
- where T : new() (parameterless constructor)
- where T : BaseClass
- where T : IInterface`,
      },
      {
        id: "1-3",
        title: "Generic Methods",
        completed: false,
        notes: "",
      },
      {
        id: "1-4",
        title: "Generic Constraints",
        completed: false,
        notes: "",
      },
      {
        id: "1-5",
        title: "Covariance & Contravariance",
        completed: false,
        notes: "",
      },
    ],
  },
  {
    id: "2",
    title: "Design Patterns",
    expanded: false,
    lessons: [
      {
        id: "2-1",
        title: "Singleton Pattern",
        completed: true,
        quizScore: 90,
        notes: `## Singleton Pattern

The Singleton pattern ensures a class has only one instance and provides a global point of access to it.

### Implementation

\`\`\`csharp
public sealed class Singleton
{
    private static Singleton instance = null;
    private static readonly object padlock = new object();

    private Singleton() { }

    public static Singleton Instance
    {
        get
        {
            lock (padlock)
            {
                if (instance == null)
                {
                    instance = new Singleton();
                }
                return instance;
            }
        }
    }
}
\`\`\``,
      },
      {
        id: "2-2",
        title: "Factory Pattern",
        completed: false,
        notes: "",
      },
      {
        id: "2-3",
        title: "Observer Pattern",
        completed: false,
        notes: "",
      },
    ],
  },
  {
    id: "3",
    title: "Data Structures",
    expanded: false,
    lessons: [
      {
        id: "3-1",
        title: "Arrays & Lists",
        completed: false,
        notes: "",
      },
      {
        id: "3-2",
        title: "Stacks & Queues",
        completed: false,
        notes: "",
      },
      {
        id: "3-3",
        title: "Trees",
        completed: false,
        notes: "",
      },
      {
        id: "3-4",
        title: "Graphs",
        completed: false,
        notes: "",
      },
    ],
  },
]

const sampleMessages: Message[] = [
  {
    id: "1",
    role: "user",
    content: "I want to learn about C# Generics",
    timestamp: new Date(),
  },
  {
    id: "2",
    role: "assistant",
    content:
      "Great choice! C# Generics are a powerful feature that allows you to write flexible, reusable code while maintaining type safety. I've created a curriculum for you in the sidebar.\n\nLet's start with the basics. A Generic is a way to define classes, methods, or interfaces that work with any data type, rather than a specific one. This means you can write code once and use it with different types.\n\nFor example, instead of creating separate classes for IntList, StringList, and CustomerList, you can create a single List<T> that works with any type.",
    timestamp: new Date(),
  },
  {
    id: "3",
    role: "user",
    content: "That makes sense. Can you show me a simple example?",
    timestamp: new Date(),
  },
  {
    id: "4",
    role: "assistant",
    content:
      "Absolutely! Here's a simple generic method that swaps two values:\n\n```csharp\npublic static void Swap<T>(ref T a, ref T b)\n{\n    T temp = a;\n    a = b;\n    b = temp;\n}\n```\n\nYou can use this with any type:\n- `Swap<int>(ref x, ref y)` for integers\n- `Swap<string>(ref s1, ref s2)` for strings\n\nThe compiler ensures type safety - you can't accidentally swap an int with a string!\n\nNow, let's test your understanding with a quick quiz:",
    timestamp: new Date(),
    quiz: {
      id: "q1",
      question:
        "What does the 'T' represent in a generic definition like List<T>?",
      options: [
        "A specific data type called T",
        "A placeholder for any type specified at usage",
        "The 'Template' keyword",
        "A type that must inherit from object",
      ],
      correctIndex: 1,
    },
  },
]

export default function OrkaAI() {
  const [topics, setTopics] = useState<Topic[]>(sampleTopics)
  const [messages, setMessages] = useState<Message[]>(sampleMessages)
  const [isTyping, setIsTyping] = useState(false)
  const [selectedLesson, setSelectedLesson] = useState<{
    topicId: string
    lessonId: string
  } | null>(null)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)

  // Calculate stats
  const stats = {
    lessonsCompleted: topics.reduce(
      (acc, t) => acc + t.lessons.filter((l) => l.completed).length,
      0
    ),
    quizzesPassed: topics.reduce(
      (acc, t) =>
        acc + t.lessons.filter((l) => l.quizScore && l.quizScore >= 60).length,
      0
    ),
    streak: 5,
  }

  // Keyboard shortcuts
  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      // Toggle sidebar with Cmd+B
      if (e.key === "b" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        setSidebarCollapsed((prev) => !prev)
      }
      // Close panels with Escape
      if (e.key === "Escape") {
        if (selectedLesson) setSelectedLesson(null)
        else if (searchOpen) setSearchOpen(false)
        else if (settingsOpen) setSettingsOpen(false)
      }
    }
    document.addEventListener("keydown", down)
    return () => document.removeEventListener("keydown", down)
  }, [selectedLesson, searchOpen, settingsOpen])

  const toggleTopic = useCallback((topicId: string) => {
    setTopics((prev) =>
      prev.map((topic) =>
        topic.id === topicId ? { ...topic, expanded: !topic.expanded } : topic
      )
    )
  }, [])

  const selectLesson = useCallback((topicId: string, lessonId: string) => {
    setSelectedLesson({ topicId, lessonId })
  }, [])

  const closeWiki = useCallback(() => {
    setSelectedLesson(null)
  }, [])

  const navigateLesson = useCallback(
    (direction: "prev" | "next") => {
      if (!selectedLesson) return

      const allLessons = topics.flatMap((topic) =>
        topic.lessons.map((lesson) => ({
          ...lesson,
          topicId: topic.id,
        }))
      )

      const currentIndex = allLessons.findIndex(
        (l) =>
          l.id === selectedLesson.lessonId &&
          l.topicId === selectedLesson.topicId
      )

      const newIndex =
        direction === "prev" ? currentIndex - 1 : currentIndex + 1

      if (newIndex >= 0 && newIndex < allLessons.length) {
        const newLesson = allLessons[newIndex]
        setSelectedLesson({ topicId: newLesson.topicId, lessonId: newLesson.id })
      }
    },
    [selectedLesson, topics]
  )

  const sendMessage = useCallback((content: string) => {
    const userMessage: Message = {
      id: Date.now().toString(),
      role: "user",
      content,
      timestamp: new Date(),
    }

    setMessages((prev) => [...prev, userMessage])
    setIsTyping(true)

    // Simulate AI response
    setTimeout(() => {
      const aiMessage: Message = {
        id: (Date.now() + 1).toString(),
        role: "assistant",
        content: getAIResponse(content),
        timestamp: new Date(),
        quiz: content.toLowerCase().includes("quiz") ? generateQuiz() : undefined,
      }
      setMessages((prev) => [...prev, aiMessage])
      setIsTyping(false)
    }, 1500)
  }, [])

  const answerQuiz = useCallback((messageId: string, selectedIndex: number) => {
    setMessages((prev) =>
      prev.map((msg) =>
        msg.id === messageId && msg.quiz
          ? { ...msg, quiz: { ...msg.quiz, selectedIndex } }
          : msg
      )
    )
  }, [])

  const getSelectedLessonData = (): {
    lesson: Lesson | null
    topicTitle: string
    hasPrev: boolean
    hasNext: boolean
  } => {
    if (!selectedLesson) return { lesson: null, topicTitle: "", hasPrev: false, hasNext: false }

    const topic = topics.find((t) => t.id === selectedLesson.topicId)
    if (!topic) return { lesson: null, topicTitle: "", hasPrev: false, hasNext: false }

    const lesson = topic.lessons.find((l) => l.id === selectedLesson.lessonId)

    const allLessons = topics.flatMap((t) =>
      t.lessons.map((l) => ({ ...l, topicId: t.id }))
    )
    const currentIndex = allLessons.findIndex(
      (l) =>
        l.id === selectedLesson.lessonId && l.topicId === selectedLesson.topicId
    )

    return {
      lesson: lesson || null,
      topicTitle: topic.title,
      hasPrev: currentIndex > 0,
      hasNext: currentIndex < allLessons.length - 1,
    }
  }

  const { lesson: currentLesson, topicTitle, hasPrev, hasNext } = getSelectedLessonData()

  return (
    <div className="relative flex flex-col h-screen overflow-hidden">
      {/* Ambient Background */}
      <AmbientBackground />

      {/* Main Content */}
      <div className="relative z-10 flex flex-col h-full">
        {/* Header */}
        <Header
          onOpenSearch={() => setSearchOpen(true)}
          onOpenSettings={() => setSettingsOpen(true)}
          stats={stats}
        />

        <div className="flex flex-1 overflow-hidden">
          {/* Mobile sidebar toggle */}
          <button
            onClick={() => setSidebarOpen(!sidebarOpen)}
            className="lg:hidden fixed bottom-4 left-4 z-50 w-14 h-14 rounded-2xl glass-card glow-soft flex items-center justify-center shadow-lg transition-transform hover:scale-105 active:scale-95"
          >
            <Menu className="w-5 h-5 text-primary" />
          </button>

          {/* Mobile sidebar backdrop */}
          {sidebarOpen && (
            <div
              className="lg:hidden fixed inset-0 bg-background/60 backdrop-blur-sm z-30"
              onClick={() => setSidebarOpen(false)}
            />
          )}

          {/* Left Sidebar - Knowledge Map */}
          <div
            className={`fixed lg:relative inset-y-0 left-0 z-40 transform transition-transform duration-300 ${
              sidebarOpen ? "translate-x-0" : "-translate-x-full lg:translate-x-0"
            }`}
          >
            <KnowledgeSidebar
              topics={topics}
              onToggleTopic={toggleTopic}
              onSelectLesson={selectLesson}
              selectedLesson={selectedLesson}
              collapsed={sidebarCollapsed}
              onToggleCollapse={() => setSidebarCollapsed(!sidebarCollapsed)}
            />
          </div>

          {/* Center Panel - Chat */}
          <ChatPanel
            messages={messages}
            onSendMessage={sendMessage}
            onAnswerQuiz={answerQuiz}
            isTyping={isTyping}
          />

          {/* Right Drawer - Wiki */}
          <WikiDrawer
            lesson={currentLesson}
            topicTitle={topicTitle}
            isOpen={!!selectedLesson}
            onClose={closeWiki}
            onNavigate={navigateLesson}
            hasPrev={hasPrev}
            hasNext={hasNext}
          />
        </div>
      </div>

      {/* Search Dialog */}
      <SearchDialog
        open={searchOpen}
        onOpenChange={setSearchOpen}
        topics={topics}
        onSelectLesson={selectLesson}
        onSendCommand={sendMessage}
      />

      {/* Settings Panel */}
      <SettingsPanel open={settingsOpen} onOpenChange={setSettingsOpen} />
    </div>
  )
}

function getAIResponse(userMessage: string): string {
  const lower = userMessage.toLowerCase()

  if (lower.includes("plan") || lower.includes("/plan")) {
    return "I've analyzed your learning goals and created a structured curriculum. You can see the topics and lessons in the sidebar on the left. Each lesson builds on the previous one, so I recommend following the order.\n\nWould you like to start with the first lesson, or do you have any questions about the curriculum?"
  }

  if (lower.includes("quiz") || lower.includes("/quiz")) {
    return "Let's test your understanding with a quiz! Here's a question:"
  }

  if (lower.includes("next") || lower.includes("continue") || lower.includes("/next")) {
    return "Let's move on to Generic Methods. While generic classes are useful, sometimes you only need a single method to be generic.\n\nGeneric methods can be defined within non-generic classes and allow you to write a single method that works with different types. This is particularly useful for utility methods like comparison, swapping, or searching.\n\n```csharp\npublic T FindMax<T>(T[] items) where T : IComparable<T>\n{\n    T max = items[0];\n    foreach (T item in items)\n    {\n        if (item.CompareTo(max) > 0)\n            max = item;\n    }\n    return max;\n}\n```"
  }

  if (lower.includes("example") || lower.includes("show") || lower.includes("/code")) {
    return "Here's a practical example of using generics in a real-world scenario:\n\n```csharp\npublic class Repository<T> where T : IEntity\n{\n    private readonly DbContext _context;\n    \n    public Repository(DbContext context)\n    {\n        _context = context;\n    }\n    \n    public T GetById(int id)\n    {\n        return _context.Set<T>().Find(id);\n    }\n    \n    public void Save(T entity)\n    {\n        _context.Set<T>().Add(entity);\n        _context.SaveChanges();\n    }\n    \n    public void Delete(T entity)\n    {\n        _context.Set<T>().Remove(entity);\n        _context.SaveChanges();\n    }\n}\n```\n\nThis pattern is commonly used in data access layers. The constraint `where T : IEntity` ensures that only types implementing the IEntity interface can be used with this repository."
  }

  if (lower.includes("summarize") || lower.includes("/summarize")) {
    return "Here's a summary of what we've covered so far:\n\n**C# Generics - Key Points:**\n\n1. **What are Generics?** Type parameters that allow you to write flexible, reusable code while maintaining type safety.\n\n2. **Benefits:**\n   - Type safety at compile time\n   - Code reusability\n   - Better performance (no boxing/unboxing)\n\n3. **Syntax:** Use `<T>` where T is the type parameter\n\n4. **Constraints:** Use `where` keyword to limit acceptable types\n\nWould you like me to save this summary to your notes?"
  }

  return "That's a great question! Let me explain further. Generics are one of the most powerful features in C#, and understanding them well will significantly improve your code quality.\n\nWould you like me to:\n- Provide more examples (`/code`)\n- Create a quiz to test your understanding (`/quiz`)\n- Summarize what we've learned (`/summarize`)\n- Move on to the next topic (`/next`)"
}

function generateQuiz() {
  const quizzes = [
    {
      id: `quiz-${Date.now()}`,
      question:
        "Which constraint would you use to ensure a generic type has a parameterless constructor?",
      options: [
        "where T : struct",
        "where T : class",
        "where T : new()",
        "where T : object",
      ],
      correctIndex: 2,
    },
    {
      id: `quiz-${Date.now()}`,
      question: "What is the main benefit of using generics over non-generic code?",
      options: [
        "Faster compilation time",
        "Smaller memory footprint",
        "Type safety with code reusability",
        "Simpler syntax",
      ],
      correctIndex: 2,
    },
    {
      id: `quiz-${Date.now()}`,
      question: "Which collection type uses generics?",
      options: [
        "ArrayList",
        "Hashtable",
        "List<T>",
        "Array",
      ],
      correctIndex: 2,
    },
  ]

  return quizzes[Math.floor(Math.random() * quizzes.length)]
}
