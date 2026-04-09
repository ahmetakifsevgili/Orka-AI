"use client"

import {
  X,
  FileText,
  CheckCircle2,
  BookOpen,
  Award,
  Copy,
  Download,
  Share2,
  Printer,
  ChevronLeft,
  ChevronRight,
  Clock,
  BarChart3,
} from "lucide-react"
import { cn } from "@/lib/utils"
import type { Lesson } from "@/lib/types"
import { Button } from "@/components/ui/button"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { Separator } from "@/components/ui/separator"

interface WikiDrawerProps {
  lesson: Lesson | null
  topicTitle: string
  isOpen: boolean
  onClose: () => void
  onNavigate?: (direction: "prev" | "next") => void
  hasPrev?: boolean
  hasNext?: boolean
}

export function WikiDrawer({
  lesson,
  topicTitle,
  isOpen,
  onClose,
  onNavigate,
  hasPrev = false,
  hasNext = false,
}: WikiDrawerProps) {
  return (
    <>
      {/* Backdrop */}
      {isOpen && (
        <div
          className="fixed inset-0 bg-background/50 backdrop-blur-sm z-40 lg:hidden"
          onClick={onClose}
        />
      )}

      {/* Drawer */}
      <aside
        className={cn(
          "fixed right-0 top-0 h-full w-full max-w-lg bg-card border-l border-border z-50 transform transition-transform duration-300 ease-out flex flex-col",
          "lg:relative lg:z-0",
          isOpen ? "translate-x-0" : "translate-x-full lg:hidden"
        )}
      >
        {lesson ? (
          <>
            {/* Header */}
            <div className="flex items-start justify-between p-4 border-b border-border">
              <div className="flex-1 pr-4">
                <p className="text-xs text-muted-foreground mb-1">{topicTitle}</p>
                <h2 className="text-lg font-semibold text-card-foreground">
                  {lesson.title}
                </h2>
                <div className="flex items-center gap-2 mt-2 flex-wrap">
                  {lesson.completed && (
                    <span className="flex items-center gap-1 text-xs text-primary bg-primary/10 px-2 py-1 rounded-full">
                      <CheckCircle2 className="w-3 h-3" />
                      Completed
                    </span>
                  )}
                  {lesson.quizScore !== undefined && (
                    <span className="flex items-center gap-1 text-xs text-muted-foreground bg-muted px-2 py-1 rounded-full">
                      <Award className="w-3 h-3" />
                      Quiz: {lesson.quizScore}%
                    </span>
                  )}
                  <span className="flex items-center gap-1 text-xs text-muted-foreground bg-muted px-2 py-1 rounded-full">
                    <Clock className="w-3 h-3" />
                    10 min read
                  </span>
                </div>
              </div>
              <div className="flex items-center gap-1">
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <button
                        onClick={() => onNavigate?.("prev")}
                        disabled={!hasPrev}
                        className="w-8 h-8 rounded-lg flex items-center justify-center hover:bg-secondary transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        <ChevronLeft className="w-4 h-4 text-muted-foreground" />
                      </button>
                    </TooltipTrigger>
                    <TooltipContent>Previous lesson</TooltipContent>
                  </Tooltip>
                </TooltipProvider>
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <button
                        onClick={() => onNavigate?.("next")}
                        disabled={!hasNext}
                        className="w-8 h-8 rounded-lg flex items-center justify-center hover:bg-secondary transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        <ChevronRight className="w-4 h-4 text-muted-foreground" />
                      </button>
                    </TooltipTrigger>
                    <TooltipContent>Next lesson</TooltipContent>
                  </Tooltip>
                </TooltipProvider>
                <Separator orientation="vertical" className="h-6 mx-1" />
                <button
                  onClick={onClose}
                  className="w-8 h-8 rounded-lg flex items-center justify-center hover:bg-secondary transition-colors"
                >
                  <X className="w-4 h-4 text-muted-foreground" />
                </button>
              </div>
            </div>

            {/* Tabs */}
            <Tabs defaultValue="notes" className="flex-1 flex flex-col overflow-hidden">
              <div className="px-4 pt-2 border-b border-border">
                <TabsList className="w-full grid grid-cols-3">
                  <TabsTrigger value="notes" className="text-xs">
                    <FileText className="w-3 h-3 mr-1.5" />
                    Notes
                  </TabsTrigger>
                  <TabsTrigger value="quiz" className="text-xs">
                    <BarChart3 className="w-3 h-3 mr-1.5" />
                    Quiz Results
                  </TabsTrigger>
                  <TabsTrigger value="resources" className="text-xs">
                    <BookOpen className="w-3 h-3 mr-1.5" />
                    Resources
                  </TabsTrigger>
                </TabsList>
              </div>

              {/* Notes Tab */}
              <TabsContent value="notes" className="flex-1 overflow-y-auto p-4 mt-0">
                {lesson.notes ? (
                  <div className="prose prose-sm prose-invert max-w-none">
                    <WikiContent content={lesson.notes} />
                  </div>
                ) : (
                  <EmptyState
                    icon={FileText}
                    title="No notes yet"
                    description="Continue learning to generate notes for this lesson."
                  />
                )}
              </TabsContent>

              {/* Quiz Results Tab */}
              <TabsContent value="quiz" className="flex-1 overflow-y-auto p-4 mt-0">
                {lesson.quizScore !== undefined ? (
                  <div className="space-y-6">
                    {/* Score Card */}
                    <div className="p-6 rounded-xl bg-secondary/50 border border-border text-center">
                      <p className="text-sm text-muted-foreground mb-2">Your Score</p>
                      <p className="text-5xl font-bold text-foreground mb-2">
                        {lesson.quizScore}%
                      </p>
                      <p
                        className={cn(
                          "text-sm font-medium",
                          lesson.quizScore >= 80
                            ? "text-primary"
                            : lesson.quizScore >= 60
                            ? "text-chart-3"
                            : "text-destructive"
                        )}
                      >
                        {lesson.quizScore >= 80
                          ? "Excellent!"
                          : lesson.quizScore >= 60
                          ? "Good job!"
                          : "Keep practicing!"}
                      </p>
                    </div>

                    {/* Stats */}
                    <div className="grid grid-cols-3 gap-4">
                      <div className="p-3 rounded-lg bg-secondary/50 text-center">
                        <p className="text-2xl font-semibold text-foreground">5</p>
                        <p className="text-xs text-muted-foreground">Questions</p>
                      </div>
                      <div className="p-3 rounded-lg bg-primary/10 text-center">
                        <p className="text-2xl font-semibold text-primary">4</p>
                        <p className="text-xs text-muted-foreground">Correct</p>
                      </div>
                      <div className="p-3 rounded-lg bg-destructive/10 text-center">
                        <p className="text-2xl font-semibold text-destructive">1</p>
                        <p className="text-xs text-muted-foreground">Wrong</p>
                      </div>
                    </div>

                    <Button variant="secondary" className="w-full">
                      Retake Quiz
                    </Button>
                  </div>
                ) : (
                  <EmptyState
                    icon={BarChart3}
                    title="No quiz taken"
                    description="Complete the quiz for this lesson to see your results."
                  />
                )}
              </TabsContent>

              {/* Resources Tab */}
              <TabsContent value="resources" className="flex-1 overflow-y-auto p-4 mt-0">
                <div className="space-y-4">
                  <ResourceItem
                    title="Official Documentation"
                    description="Microsoft C# Generics Guide"
                    type="link"
                  />
                  <ResourceItem
                    title="Video Tutorial"
                    description="15 min introduction to generics"
                    type="video"
                  />
                  <ResourceItem
                    title="Practice Exercises"
                    description="5 coding challenges"
                    type="code"
                  />
                </div>
              </TabsContent>
            </Tabs>

            {/* Footer */}
            <div className="p-4 border-t border-border">
              <div className="flex items-center gap-2 mb-3">
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Button variant="ghost" size="icon" className="h-9 w-9">
                        <Copy className="w-4 h-4" />
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent>Copy notes</TooltipContent>
                  </Tooltip>
                </TooltipProvider>
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Button variant="ghost" size="icon" className="h-9 w-9">
                        <Download className="w-4 h-4" />
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent>Download as PDF</TooltipContent>
                  </Tooltip>
                </TooltipProvider>
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Button variant="ghost" size="icon" className="h-9 w-9">
                        <Share2 className="w-4 h-4" />
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent>Share</TooltipContent>
                  </Tooltip>
                </TooltipProvider>
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Button variant="ghost" size="icon" className="h-9 w-9">
                        <Printer className="w-4 h-4" />
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent>Print</TooltipContent>
                  </Tooltip>
                </TooltipProvider>
              </div>
              <Button className="w-full">
                <BookOpen className="w-4 h-4 mr-2" />
                Continue Learning
              </Button>
            </div>
          </>
        ) : (
          <div className="flex flex-col items-center justify-center h-full text-center px-4">
            <div className="w-12 h-12 rounded-full bg-muted flex items-center justify-center mb-3">
              <BookOpen className="w-6 h-6 text-muted-foreground" />
            </div>
            <p className="text-sm text-muted-foreground">
              Select a lesson from the sidebar to view its notes
            </p>
          </div>
        )}
      </aside>
    </>
  )
}

interface EmptyStateProps {
  icon: typeof FileText
  title: string
  description: string
}

function EmptyState({ icon: Icon, title, description }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center justify-center h-full text-center">
      <div className="w-12 h-12 rounded-full bg-muted flex items-center justify-center mb-3">
        <Icon className="w-6 h-6 text-muted-foreground" />
      </div>
      <p className="text-sm font-medium text-foreground mb-1">{title}</p>
      <p className="text-xs text-muted-foreground">{description}</p>
    </div>
  )
}

interface ResourceItemProps {
  title: string
  description: string
  type: "link" | "video" | "code"
}

function ResourceItem({ title, description, type }: ResourceItemProps) {
  return (
    <button className="w-full flex items-center gap-3 p-3 rounded-lg bg-secondary/50 border border-border hover:bg-secondary transition-colors text-left">
      <div className="w-10 h-10 rounded-lg bg-primary/10 flex items-center justify-center">
        {type === "link" && <FileText className="w-5 h-5 text-primary" />}
        {type === "video" && <BookOpen className="w-5 h-5 text-primary" />}
        {type === "code" && <BarChart3 className="w-5 h-5 text-primary" />}
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium text-foreground truncate">{title}</p>
        <p className="text-xs text-muted-foreground">{description}</p>
      </div>
    </button>
  )
}

function WikiContent({ content }: { content: string }) {
  // Simple markdown-like rendering
  const sections = content.split("\n\n")

  return (
    <div className="space-y-4">
      {sections.map((section, index) => {
        if (section.startsWith("# ")) {
          return (
            <h1 key={index} className="text-xl font-bold text-card-foreground">
              {section.slice(2)}
            </h1>
          )
        }
        if (section.startsWith("## ")) {
          return (
            <h2
              key={index}
              className="text-lg font-semibold text-card-foreground mt-6 pb-2 border-b border-border"
            >
              {section.slice(3)}
            </h2>
          )
        }
        if (section.startsWith("### ")) {
          return (
            <h3 key={index} className="text-base font-medium text-card-foreground mt-4">
              {section.slice(4)}
            </h3>
          )
        }
        if (section.startsWith("- ")) {
          const items = section.split("\n").filter((line) => line.startsWith("- "))
          return (
            <ul key={index} className="space-y-2">
              {items.map((item, i) => (
                <li key={i} className="flex items-start gap-2 text-muted-foreground">
                  <span className="w-1.5 h-1.5 rounded-full bg-primary mt-2 flex-shrink-0" />
                  <span className="text-sm">{item.slice(2)}</span>
                </li>
              ))}
            </ul>
          )
        }
        if (section.startsWith("```")) {
          const code = section.slice(3, -3).trim()
          const lines = code.split("\n")
          const language = lines[0]?.match(/^[a-z]+$/i) ? lines.shift() : ""
          return (
            <div key={index} className="rounded-lg overflow-hidden border border-border">
              {language && (
                <div className="px-3 py-1.5 bg-secondary border-b border-border text-xs text-muted-foreground">
                  {language}
                </div>
              )}
              <pre className="bg-secondary/50 p-4 overflow-x-auto">
                <code className="text-xs text-foreground font-mono">
                  {lines.join("\n")}
                </code>
              </pre>
            </div>
          )
        }
        return (
          <p key={index} className="text-sm text-muted-foreground leading-relaxed">
            {section}
          </p>
        )
      })}
    </div>
  )
}
