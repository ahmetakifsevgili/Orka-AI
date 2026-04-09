"use client"

import {
  ChevronDown,
  ChevronRight,
  BookOpen,
  CheckCircle2,
  Circle,
  Plus,
  GraduationCap,
  MoreHorizontal,
  Pencil,
  Trash2,
  FolderPlus,
  PanelLeftClose,
  PanelLeft,
  Trophy,
  Flame,
  Sparkles,
  Layers,
  Zap,
} from "lucide-react"
import { cn } from "@/lib/utils"
import type { Topic, Lesson } from "@/lib/types"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"

interface KnowledgeSidebarProps {
  topics: Topic[]
  onToggleTopic: (topicId: string) => void
  onSelectLesson: (topicId: string, lessonId: string) => void
  selectedLesson: { topicId: string; lessonId: string } | null
  collapsed?: boolean
  onToggleCollapse?: () => void
}

export function KnowledgeSidebar({
  topics,
  onToggleTopic,
  onSelectLesson,
  selectedLesson,
  collapsed = false,
  onToggleCollapse,
}: KnowledgeSidebarProps) {
  const totalLessons = topics.reduce((acc, t) => acc + t.lessons.length, 0)
  const completedLessons = topics.reduce(
    (acc, t) => acc + t.lessons.filter((l) => l.completed).length,
    0
  )
  const progressPercent = totalLessons > 0 ? (completedLessons / totalLessons) * 100 : 0

  if (collapsed) {
    return (
      <TooltipProvider>
        <aside className="w-20 h-full glass border-r border-border/30 flex flex-col">
          <div className="p-4 border-b border-border/30">
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  onClick={onToggleCollapse}
                  className="w-12 h-12 rounded-xl gradient-primary flex items-center justify-center hover:glow-soft transition-all"
                >
                  <PanelLeft className="w-5 h-5 text-primary-foreground" />
                </button>
              </TooltipTrigger>
              <TooltipContent side="right" className="glass-card">Expand sidebar</TooltipContent>
            </Tooltip>
          </div>
          <div className="flex-1 overflow-y-auto p-3 space-y-3">
            {topics.map((topic, index) => {
              const topicProgress = topic.lessons.length > 0 
                ? (topic.lessons.filter(l => l.completed).length / topic.lessons.length) * 100 
                : 0
              return (
                <Tooltip key={topic.id}>
                  <TooltipTrigger asChild>
                    <button
                      onClick={() => onToggleTopic(topic.id)}
                      className="relative w-12 h-12 rounded-xl glass-card flex items-center justify-center hover:glow-border transition-all group"
                    >
                      <span className="text-sm font-bold gradient-text">
                        {topic.title.slice(0, 2).toUpperCase()}
                      </span>
                      {/* Progress ring */}
                      <svg className="absolute inset-0 w-12 h-12 -rotate-90">
                        <circle
                          cx="24"
                          cy="24"
                          r="20"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2"
                          className="text-border/30"
                        />
                        <circle
                          cx="24"
                          cy="24"
                          r="20"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2"
                          strokeDasharray={`${(topicProgress / 100) * 126} 126`}
                          className="text-primary transition-all duration-500"
                        />
                      </svg>
                    </button>
                  </TooltipTrigger>
                  <TooltipContent side="right" className="glass-card">
                    <div>
                      <p className="font-medium">{topic.title}</p>
                      <p className="text-xs text-muted-foreground">{Math.round(topicProgress)}% complete</p>
                    </div>
                  </TooltipContent>
                </Tooltip>
              )
            })}
          </div>
          <div className="p-3 border-t border-border/30">
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="w-12 h-12 rounded-xl glass-card flex items-center justify-center hover:glow-border transition-all">
                  <Plus className="w-5 h-5 text-primary" />
                </button>
              </TooltipTrigger>
              <TooltipContent side="right" className="glass-card">New Topic</TooltipContent>
            </Tooltip>
          </div>
        </aside>
      </TooltipProvider>
    )
  }

  return (
    <aside className="w-80 h-full glass border-r border-border/30 flex flex-col">
      {/* Header */}
      <div className="p-5 border-b border-border/30">
        <div className="flex items-center justify-between mb-5">
          <div className="flex items-center gap-3">
            <div className="w-11 h-11 rounded-xl gradient-primary flex items-center justify-center glow-soft">
              <Layers className="w-6 h-6 text-primary-foreground" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-foreground tracking-tight">Knowledge Map</h1>
              <p className="text-xs text-muted-foreground">{topics.length} topics</p>
            </div>
          </div>
          <button
            onClick={onToggleCollapse}
            className="w-9 h-9 rounded-xl flex items-center justify-center hover:bg-secondary/50 transition-colors"
          >
            <PanelLeftClose className="w-4 h-4 text-muted-foreground" />
          </button>
        </div>

        {/* Progress Ring */}
        {totalLessons > 0 && (
          <div className="flex items-center gap-4 p-4 rounded-2xl glass-card">
            <div className="relative w-16 h-16">
              <svg className="w-16 h-16 -rotate-90">
                <circle
                  cx="32"
                  cy="32"
                  r="28"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="4"
                  className="text-border/30"
                />
                <circle
                  cx="32"
                  cy="32"
                  r="28"
                  fill="none"
                  stroke="url(#progress-gradient)"
                  strokeWidth="4"
                  strokeLinecap="round"
                  strokeDasharray={`${(progressPercent / 100) * 176} 176`}
                  className="transition-all duration-700 ease-out"
                />
                <defs>
                  <linearGradient id="progress-gradient" x1="0%" y1="0%" x2="100%" y2="100%">
                    <stop offset="0%" stopColor="oklch(0.72 0.17 195)" />
                    <stop offset="100%" stopColor="oklch(0.75 0.15 145)" />
                  </linearGradient>
                </defs>
              </svg>
              <div className="absolute inset-0 flex items-center justify-center">
                <span className="text-lg font-bold gradient-text">{Math.round(progressPercent)}%</span>
              </div>
            </div>
            <div className="flex-1">
              <p className="text-sm font-medium text-foreground">Overall Progress</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                {completedLessons} of {totalLessons} lessons completed
              </p>
              <div className="flex items-center gap-2 mt-2">
                <div className="flex items-center gap-1 px-2 py-0.5 rounded-full bg-chart-3/20 text-chart-3">
                  <Zap className="w-3 h-3" />
                  <span className="text-[10px] font-medium">On track</span>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Quick Stats */}
        <div className="grid grid-cols-2 gap-3 mt-4">
          <div className="flex items-center gap-3 p-3 rounded-xl glass-card hover-lift">
            <div className="w-9 h-9 rounded-lg bg-chart-3/20 flex items-center justify-center">
              <Trophy className="w-4 h-4 text-chart-3" />
            </div>
            <div>
              <p className="text-[10px] text-muted-foreground uppercase tracking-wider">Completed</p>
              <p className="text-lg font-bold text-foreground">{completedLessons}</p>
            </div>
          </div>
          <div className="flex items-center gap-3 p-3 rounded-xl glass-card hover-lift">
            <div className="w-9 h-9 rounded-lg bg-chart-4/20 flex items-center justify-center">
              <Flame className="w-4 h-4 text-chart-4" />
            </div>
            <div>
              <p className="text-[10px] text-muted-foreground uppercase tracking-wider">Streak</p>
              <p className="text-lg font-bold text-foreground">5 days</p>
            </div>
          </div>
        </div>
      </div>

      {/* Topics Tree */}
      <div className="flex-1 overflow-y-auto p-4">
        {topics.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-center px-4">
            <div className="w-16 h-16 rounded-2xl glass-card flex items-center justify-center mb-4">
              <Sparkles className="w-8 h-8 text-primary" />
            </div>
            <h3 className="text-sm font-medium text-foreground mb-2">No topics yet</h3>
            <p className="text-xs text-muted-foreground mb-4 max-w-[200px]">
              Start a conversation to build your knowledge map
            </p>
            <div className="px-3 py-1.5 rounded-full glass-card text-xs text-muted-foreground">
              Try: &quot;I want to learn C# Generics&quot;
            </div>
          </div>
        ) : (
          <div className="space-y-2">
            {topics.map((topic) => (
              <TopicItem
                key={topic.id}
                topic={topic}
                onToggle={() => onToggleTopic(topic.id)}
                onSelectLesson={(lessonId) => onSelectLesson(topic.id, lessonId)}
                selectedLesson={selectedLesson}
              />
            ))}
          </div>
        )}
      </div>

      {/* Footer */}
      <div className="p-4 border-t border-border/30 space-y-3">
        <button className="w-full flex items-center justify-center gap-2 px-4 py-3 rounded-xl gradient-primary hover:glow-soft text-primary-foreground text-sm font-medium transition-all hover-lift">
          <Plus className="w-4 h-4" />
          New Topic
        </button>
        <button className="w-full flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl glass-card hover:glow-border text-foreground text-sm transition-all">
          <FolderPlus className="w-4 h-4 text-muted-foreground" />
          Import Curriculum
        </button>
      </div>
    </aside>
  )
}

interface TopicItemProps {
  topic: Topic
  onToggle: () => void
  onSelectLesson: (lessonId: string) => void
  selectedLesson: { topicId: string; lessonId: string } | null
}

function TopicItem({ topic, onToggle, onSelectLesson, selectedLesson }: TopicItemProps) {
  const completedCount = topic.lessons.filter((l) => l.completed).length
  const totalCount = topic.lessons.length
  const progressPercent = totalCount > 0 ? (completedCount / totalCount) * 100 : 0

  return (
    <div className="rounded-xl overflow-hidden">
      <div className="flex items-center gap-1 group">
        <button
          onClick={onToggle}
          className={cn(
            "flex-1 flex items-center gap-3 px-3 py-2.5 rounded-xl transition-all",
            topic.expanded ? "glass-card" : "hover:bg-secondary/30"
          )}
        >
          <div className={cn(
            "w-8 h-8 rounded-lg flex items-center justify-center transition-colors",
            topic.expanded ? "bg-primary/20" : "bg-secondary/50"
          )}>
            {topic.expanded ? (
              <ChevronDown className="w-4 h-4 text-primary" />
            ) : (
              <ChevronRight className="w-4 h-4 text-muted-foreground" />
            )}
          </div>
          <div className="flex-1 text-left">
            <span className="text-sm font-medium text-foreground">{topic.title}</span>
            <div className="flex items-center gap-2 mt-0.5">
              <div className="flex-1 h-1 bg-border/30 rounded-full overflow-hidden max-w-[100px]">
                <div 
                  className="h-full gradient-primary transition-all duration-500"
                  style={{ width: `${progressPercent}%` }}
                />
              </div>
              <span className="text-[10px] text-muted-foreground">
                {completedCount}/{totalCount}
              </span>
            </div>
          </div>
        </button>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button className="w-8 h-8 rounded-lg flex items-center justify-center opacity-0 group-hover:opacity-100 hover:bg-secondary/50 transition-all">
              <MoreHorizontal className="w-4 h-4 text-muted-foreground" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-48 glass-card border-border/50">
            <DropdownMenuItem className="cursor-pointer">
              <Pencil className="w-4 h-4 mr-2 text-muted-foreground" />
              Rename Topic
            </DropdownMenuItem>
            <DropdownMenuItem className="cursor-pointer">
              <Plus className="w-4 h-4 mr-2 text-muted-foreground" />
              Add Lesson
            </DropdownMenuItem>
            <DropdownMenuSeparator className="bg-border/50" />
            <DropdownMenuItem className="text-destructive focus:text-destructive cursor-pointer">
              <Trash2 className="w-4 h-4 mr-2" />
              Delete Topic
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {topic.expanded && topic.lessons.length > 0 && (
        <div className="ml-5 pl-4 border-l-2 border-primary/20 space-y-1 mt-2 mb-2">
          {topic.lessons.map((lesson, index) => (
            <LessonItem
              key={lesson.id}
              lesson={lesson}
              isSelected={
                selectedLesson?.topicId === topic.id &&
                selectedLesson?.lessonId === lesson.id
              }
              onSelect={() => onSelectLesson(lesson.id)}
              index={index}
            />
          ))}
        </div>
      )}
    </div>
  )
}

interface LessonItemProps {
  lesson: Lesson
  isSelected: boolean
  onSelect: () => void
  index: number
}

function LessonItem({ lesson, isSelected, onSelect, index }: LessonItemProps) {
  return (
    <button
      onClick={onSelect}
      className={cn(
        "w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm transition-all group",
        isSelected
          ? "glass-card glow-border text-foreground"
          : "text-muted-foreground hover:text-foreground hover:bg-secondary/30"
      )}
      style={{ animationDelay: `${index * 50}ms` }}
    >
      <div className={cn(
        "w-6 h-6 rounded-full flex items-center justify-center flex-shrink-0 transition-all",
        lesson.completed 
          ? "bg-chart-3/20" 
          : isSelected 
            ? "bg-primary/20 ring-2 ring-primary/30" 
            : "bg-secondary/50"
      )}>
        {lesson.completed ? (
          <CheckCircle2 className="w-4 h-4 text-chart-3" />
        ) : (
          <Circle className={cn("w-3 h-3", isSelected ? "text-primary" : "text-muted-foreground")} />
        )}
      </div>
      <span className="truncate text-left flex-1 font-medium">{lesson.title}</span>
      {lesson.quizScore !== undefined && (
        <div className={cn(
          "px-2 py-0.5 rounded-full text-[10px] font-medium",
          lesson.quizScore >= 80 
            ? "bg-chart-3/20 text-chart-3" 
            : lesson.quizScore >= 60 
              ? "bg-chart-4/20 text-chart-4"
              : "bg-destructive/20 text-destructive"
        )}>
          {lesson.quizScore}%
        </div>
      )}
    </button>
  )
}
