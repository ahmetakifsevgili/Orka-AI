"use client"

import { useState, useEffect, useMemo } from "react"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from "@/components/ui/command"
import {
  Dialog,
  DialogContent,
} from "@/components/ui/dialog"
import {
  BookOpen,
  FileText,
  MessageSquare,
  Sparkles,
  Search,
  Clock,
  ArrowRight,
  Wand2,
} from "lucide-react"
import type { Topic } from "@/lib/types"

interface SearchDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  topics: Topic[]
  onSelectLesson: (topicId: string, lessonId: string) => void
  onSendCommand: (command: string) => void
}

export function SearchDialog({
  open,
  onOpenChange,
  topics,
  onSelectLesson,
  onSendCommand,
}: SearchDialogProps) {
  const [search, setSearch] = useState("")

  // Reset search when dialog closes
  useEffect(() => {
    if (!open) setSearch("")
  }, [open])

  // Keyboard shortcut to open
  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      if (e.key === "k" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        onOpenChange(!open)
      }
    }
    document.addEventListener("keydown", down)
    return () => document.removeEventListener("keydown", down)
  }, [open, onOpenChange])

  const commands = [
    { id: "plan", label: "Create a learning plan", icon: Wand2, command: "/plan" },
    { id: "quiz", label: "Start a quiz", icon: Sparkles, command: "/quiz" },
    { id: "summarize", label: "Summarize current topic", icon: FileText, command: "/summarize" },
    { id: "next", label: "Go to next lesson", icon: ArrowRight, command: "/next" },
  ]

  const recentSearches = [
    "C# Generics",
    "Design Patterns",
    "Generic Constraints",
  ]

  const allLessons = useMemo(() => {
    return topics.flatMap((topic) =>
      topic.lessons.map((lesson) => ({
        ...lesson,
        topicId: topic.id,
        topicTitle: topic.title,
      }))
    )
  }, [topics])

  const filteredLessons = useMemo(() => {
    if (!search) return allLessons.slice(0, 5)
    const lower = search.toLowerCase()
    return allLessons.filter(
      (lesson) =>
        lesson.title.toLowerCase().includes(lower) ||
        lesson.topicTitle.toLowerCase().includes(lower)
    )
  }, [search, allLessons])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="p-0 gap-0 max-w-xl overflow-hidden">
        <Command className="rounded-lg border-0">
          <div className="flex items-center border-b border-border px-3">
            <Search className="w-4 h-4 text-muted-foreground mr-2" />
            <CommandInput
              placeholder="Search topics, lessons, or type a command..."
              value={search}
              onValueChange={setSearch}
              className="border-0 focus:ring-0"
            />
          </div>
          <CommandList className="max-h-[400px]">
            <CommandEmpty className="py-6 text-center text-sm text-muted-foreground">
              No results found. Try a different search term.
            </CommandEmpty>

            {/* Commands */}
            {!search && (
              <>
                <CommandGroup heading="Commands">
                  {commands.map((cmd) => (
                    <CommandItem
                      key={cmd.id}
                      onSelect={() => {
                        onSendCommand(cmd.command)
                        onOpenChange(false)
                      }}
                      className="flex items-center gap-3 py-2.5"
                    >
                      <div className="w-8 h-8 rounded-lg bg-primary/10 flex items-center justify-center">
                        <cmd.icon className="w-4 h-4 text-primary" />
                      </div>
                      <div className="flex-1">
                        <p className="text-sm font-medium">{cmd.label}</p>
                        <p className="text-xs text-muted-foreground">{cmd.command}</p>
                      </div>
                    </CommandItem>
                  ))}
                </CommandGroup>
                <CommandSeparator />
              </>
            )}

            {/* Recent Searches */}
            {!search && (
              <>
                <CommandGroup heading="Recent">
                  {recentSearches.map((item) => (
                    <CommandItem
                      key={item}
                      onSelect={() => setSearch(item)}
                      className="flex items-center gap-3 py-2"
                    >
                      <Clock className="w-4 h-4 text-muted-foreground" />
                      <span className="text-sm">{item}</span>
                    </CommandItem>
                  ))}
                </CommandGroup>
                <CommandSeparator />
              </>
            )}

            {/* Lessons */}
            <CommandGroup heading={search ? "Results" : "Lessons"}>
              {filteredLessons.map((lesson) => (
                <CommandItem
                  key={lesson.id}
                  onSelect={() => {
                    onSelectLesson(lesson.topicId, lesson.id)
                    onOpenChange(false)
                  }}
                  className="flex items-center gap-3 py-2.5"
                >
                  <div className="w-8 h-8 rounded-lg bg-secondary flex items-center justify-center">
                    <BookOpen className="w-4 h-4 text-muted-foreground" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium truncate">{lesson.title}</p>
                    <p className="text-xs text-muted-foreground">{lesson.topicTitle}</p>
                  </div>
                  {lesson.completed && (
                    <span className="text-xs text-primary">Completed</span>
                  )}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </DialogContent>
    </Dialog>
  )
}
