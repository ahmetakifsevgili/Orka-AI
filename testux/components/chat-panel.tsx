"use client"

import { useState, useRef, useEffect } from "react"
import {
  Send,
  Sparkles,
  Bot,
  User,
  Paperclip,
  Mic,
  Copy,
  RefreshCw,
  ThumbsUp,
  ThumbsDown,
  Lightbulb,
  Code,
  FileText,
  Wand2,
  ArrowRight,
  Zap,
  BookOpen,
  Brain,
  Check,
} from "lucide-react"
import { cn } from "@/lib/utils"
import type { Message } from "@/lib/types"
import { QuizCard } from "./quiz-card"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"

interface ChatPanelProps {
  messages: Message[]
  onSendMessage: (content: string) => void
  onAnswerQuiz: (messageId: string, selectedIndex: number) => void
  isTyping: boolean
}

export function ChatPanel({
  messages,
  onSendMessage,
  onAnswerQuiz,
  isTyping,
}: ChatPanelProps) {
  const [input, setInput] = useState("")
  const [showCommands, setShowCommands] = useState(false)
  const [copied, setCopied] = useState<string | null>(null)
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" })
  }, [messages, isTyping])

  useEffect(() => {
    setShowCommands(input.startsWith("/"))
  }, [input])

  // Keyboard shortcut to focus input
  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      if (e.key === "/" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        inputRef.current?.focus()
      }
    }
    document.addEventListener("keydown", down)
    return () => document.removeEventListener("keydown", down)
  }, [])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (input.trim()) {
      onSendMessage(input.trim())
      setInput("")
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault()
      handleSubmit(e)
    }
  }

  const handleCopy = async (text: string, id: string) => {
    await navigator.clipboard.writeText(text)
    setCopied(id)
    setTimeout(() => setCopied(null), 2000)
  }

  const commands = [
    { command: "/plan", label: "Create a learning plan", icon: Wand2, color: "text-primary" },
    { command: "/quiz", label: "Start a quiz", icon: Sparkles, color: "text-chart-4" },
    { command: "/summarize", label: "Summarize topic", icon: FileText, color: "text-chart-3" },
    { command: "/code", label: "Show code example", icon: Code, color: "text-chart-2" },
    { command: "/hint", label: "Get a hint", icon: Lightbulb, color: "text-chart-5" },
  ]

  const filteredCommands = commands.filter((cmd) =>
    cmd.command.startsWith(input.toLowerCase())
  )

  return (
    <div className="flex-1 flex flex-col h-full">
      {/* Messages Area */}
      <div className="flex-1 overflow-y-auto">
        {messages.length === 0 ? (
          <WelcomeScreen onSuggestionClick={(text) => onSendMessage(text)} />
        ) : (
          <div className="max-w-3xl mx-auto py-8 px-4 space-y-8">
            {messages.map((message, index) => (
              <MessageBubble
                key={message.id}
                message={message}
                onAnswerQuiz={(selectedIndex) =>
                  onAnswerQuiz(message.id, selectedIndex)
                }
                onCopy={handleCopy}
                isCopied={copied === message.id}
                animationDelay={index * 100}
              />
            ))}
            {isTyping && <TypingIndicator />}
            <div ref={messagesEndRef} />
          </div>
        )}
      </div>

      {/* Input Area */}
      <div className="border-t border-border/30 p-4 glass-subtle relative">
        {/* Command Menu */}
        {showCommands && filteredCommands.length > 0 && (
          <div className="absolute bottom-full left-4 right-4 max-w-3xl mx-auto mb-3">
            <div className="glass-card rounded-2xl border border-border/50 p-2 shadow-xl">
              <p className="text-[10px] uppercase tracking-wider text-muted-foreground px-3 py-2">Commands</p>
              {filteredCommands.map((cmd) => (
                <button
                  key={cmd.command}
                  onClick={() => {
                    setInput(cmd.command + " ")
                    inputRef.current?.focus()
                  }}
                  className="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl hover:bg-secondary/50 transition-all text-left group"
                >
                  <div className={cn("w-9 h-9 rounded-lg glass-card flex items-center justify-center", cmd.color)}>
                    <cmd.icon className="w-4 h-4" />
                  </div>
                  <div className="flex-1">
                    <p className="text-sm font-medium text-foreground">{cmd.command}</p>
                    <p className="text-xs text-muted-foreground">{cmd.label}</p>
                  </div>
                  <ArrowRight className="w-4 h-4 text-muted-foreground opacity-0 group-hover:opacity-100 transition-opacity" />
                </button>
              ))}
            </div>
          </div>
        )}

        <form onSubmit={handleSubmit} className="max-w-3xl mx-auto">
          <div className="relative flex items-end gap-2 glass-card rounded-2xl p-2 focus-within:glow-border transition-all">
            {/* Attachment button */}
            <TooltipProvider>
              <Tooltip>
                <TooltipTrigger asChild>
                  <button
                    type="button"
                    className="flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center hover:bg-secondary/50 transition-colors text-muted-foreground hover:text-foreground"
                  >
                    <Paperclip className="w-4 h-4" />
                  </button>
                </TooltipTrigger>
                <TooltipContent className="glass-card">Attach file</TooltipContent>
              </Tooltip>
            </TooltipProvider>

            <textarea
              ref={inputRef}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Ask Orka anything... Type / for commands"
              rows={1}
              className="flex-1 bg-transparent border-0 resize-none text-sm text-foreground placeholder:text-muted-foreground focus:outline-none min-h-[44px] max-h-32 py-3 px-2"
              style={{ height: "auto" }}
              onInput={(e) => {
                const target = e.target as HTMLTextAreaElement
                target.style.height = "auto"
                target.style.height = `${Math.min(target.scrollHeight, 128)}px`
              }}
            />

            {/* Voice input */}
            <TooltipProvider>
              <Tooltip>
                <TooltipTrigger asChild>
                  <button
                    type="button"
                    className="flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center hover:bg-secondary/50 transition-colors text-muted-foreground hover:text-foreground"
                  >
                    <Mic className="w-4 h-4" />
                  </button>
                </TooltipTrigger>
                <TooltipContent className="glass-card">Voice input</TooltipContent>
              </Tooltip>
            </TooltipProvider>

            <button
              type="submit"
              disabled={!input.trim()}
              className={cn(
                "flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center transition-all",
                input.trim()
                  ? "gradient-primary text-primary-foreground glow-soft hover:scale-105"
                  : "bg-muted text-muted-foreground cursor-not-allowed"
              )}
            >
              <Send className="w-4 h-4" />
            </button>
          </div>
          <div className="flex items-center justify-center gap-4 mt-3">
            <p className="text-xs text-muted-foreground">
              <kbd className="px-1.5 py-0.5 rounded-md glass-card text-foreground text-[10px]">Enter</kbd> to send
            </p>
            <span className="text-muted-foreground/50">|</span>
            <p className="text-xs text-muted-foreground">
              <kbd className="px-1.5 py-0.5 rounded-md glass-card text-foreground text-[10px]">Shift + Enter</kbd> for new line
            </p>
          </div>
        </form>
      </div>
    </div>
  )
}

interface WelcomeScreenProps {
  onSuggestionClick: (text: string) => void
}

function WelcomeScreen({ onSuggestionClick }: WelcomeScreenProps) {
  const suggestions = [
    {
      icon: Code,
      title: "Learn Programming",
      description: "C#, Python, JavaScript and more",
      prompt: "I want to learn C# Generics",
      color: "from-primary to-chart-3",
    },
    {
      icon: Brain,
      title: "Design Patterns",
      description: "Master software architecture",
      prompt: "Teach me about design patterns",
      color: "from-chart-2 to-primary",
    },
    {
      icon: BookOpen,
      title: "Data Structures",
      description: "Arrays, trees, graphs",
      prompt: "Explain data structures to me",
      color: "from-chart-3 to-chart-4",
    },
    {
      icon: Sparkles,
      title: "Machine Learning",
      description: "AI and ML fundamentals",
      prompt: "I want to understand machine learning basics",
      color: "from-chart-4 to-chart-5",
    },
  ]

  return (
    <div className="flex flex-col items-center justify-center h-full text-center px-4 py-12">
      {/* Animated logo */}
      <div className="relative mb-8">
        <div className="w-24 h-24 rounded-3xl gradient-primary flex items-center justify-center glow-primary animate-pulse">
          <Sparkles className="w-12 h-12 text-primary-foreground" />
        </div>
        {/* Orbiting dots */}
        <div className="absolute inset-0 animate-spin" style={{ animationDuration: '8s' }}>
          <div className="absolute -top-2 left-1/2 -translate-x-1/2 w-3 h-3 rounded-full bg-chart-3" />
        </div>
        <div className="absolute inset-0 animate-spin" style={{ animationDuration: '12s', animationDirection: 'reverse' }}>
          <div className="absolute top-1/2 -right-2 -translate-y-1/2 w-2 h-2 rounded-full bg-chart-4" />
        </div>
      </div>

      <h2 className="text-4xl font-bold text-foreground mb-3 tracking-tight">
        Welcome to <span className="gradient-text">Orka AI</span>
      </h2>
      <p className="text-muted-foreground max-w-lg mb-12 text-lg leading-relaxed">
        Your intelligent learning companion. Tell me what you want to learn, and
        I&apos;ll create a personalized curriculum just for you.
      </p>

      <div className="grid sm:grid-cols-2 gap-4 w-full max-w-2xl">
        {suggestions.map((suggestion, index) => (
          <button
            key={suggestion.title}
            onClick={() => onSuggestionClick(suggestion.prompt)}
            className="flex items-start gap-4 p-5 rounded-2xl glass-card hover:glow-border transition-all text-left group hover-lift"
            style={{ animationDelay: `${index * 100}ms` }}
          >
            <div className={cn(
              "w-12 h-12 rounded-xl flex items-center justify-center flex-shrink-0 bg-gradient-to-br transition-transform group-hover:scale-110",
              suggestion.color
            )}>
              <suggestion.icon className="w-6 h-6 text-primary-foreground" />
            </div>
            <div className="flex-1">
              <h3 className="font-semibold text-foreground mb-1 group-hover:text-primary transition-colors">
                {suggestion.title}
              </h3>
              <p className="text-sm text-muted-foreground">{suggestion.description}</p>
            </div>
            <ArrowRight className="w-5 h-5 text-muted-foreground opacity-0 group-hover:opacity-100 group-hover:translate-x-1 transition-all mt-1" />
          </button>
        ))}
      </div>

      <div className="mt-12 flex flex-wrap justify-center gap-3">
        {["Personalized learning", "Interactive quizzes", "Auto-generated notes", "Progress tracking"].map((tag) => (
          <span key={tag} className="px-4 py-2 rounded-full glass-card text-xs text-muted-foreground">
            {tag}
          </span>
        ))}
      </div>
    </div>
  )
}

interface MessageBubbleProps {
  message: Message
  onAnswerQuiz: (selectedIndex: number) => void
  onCopy: (text: string, id: string) => void
  isCopied: boolean
  animationDelay: number
}

function MessageBubble({ message, onAnswerQuiz, onCopy, isCopied, animationDelay }: MessageBubbleProps) {
  const isUser = message.role === "user"

  return (
    <div 
      className={cn("flex gap-4 message-animate-in", isUser && "flex-row-reverse")}
      style={{ animationDelay: `${animationDelay}ms` }}
    >
      <div
        className={cn(
          "w-10 h-10 rounded-xl flex-shrink-0 flex items-center justify-center",
          isUser 
            ? "glass-card" 
            : "gradient-primary glow-soft"
        )}
      >
        {isUser ? (
          <User className="w-5 h-5 text-foreground" />
        ) : (
          <Bot className="w-5 h-5 text-primary-foreground" />
        )}
      </div>
      <div className={cn("flex-1 space-y-3 max-w-[85%]", isUser && "flex flex-col items-end")}>
        <div className="group relative">
          <div
            className={cn(
              "rounded-2xl px-5 py-4 text-sm leading-relaxed inline-block",
              isUser
                ? "gradient-primary text-primary-foreground"
                : "glass-card text-foreground"
            )}
          >
            <FormattedContent content={message.content} />
          </div>

          {/* Message actions */}
          {!isUser && (
            <div className="absolute -bottom-2 left-4 opacity-0 group-hover:opacity-100 transition-all duration-200 flex items-center gap-1 pt-2">
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <button 
                      onClick={() => onCopy(message.content, message.id)}
                      className="w-8 h-8 rounded-lg glass-card flex items-center justify-center hover:glow-border transition-all"
                    >
                      {isCopied ? (
                        <Check className="w-3.5 h-3.5 text-chart-3" />
                      ) : (
                        <Copy className="w-3.5 h-3.5 text-muted-foreground" />
                      )}
                    </button>
                  </TooltipTrigger>
                  <TooltipContent className="glass-card">{isCopied ? "Copied!" : "Copy"}</TooltipContent>
                </Tooltip>
              </TooltipProvider>
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <button className="w-8 h-8 rounded-lg glass-card flex items-center justify-center hover:glow-border transition-all">
                      <RefreshCw className="w-3.5 h-3.5 text-muted-foreground" />
                    </button>
                  </TooltipTrigger>
                  <TooltipContent className="glass-card">Regenerate</TooltipContent>
                </Tooltip>
              </TooltipProvider>
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <button className="w-8 h-8 rounded-lg glass-card flex items-center justify-center hover:glow-border transition-all">
                      <ThumbsUp className="w-3.5 h-3.5 text-muted-foreground" />
                    </button>
                  </TooltipTrigger>
                  <TooltipContent className="glass-card">Good response</TooltipContent>
                </Tooltip>
              </TooltipProvider>
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <button className="w-8 h-8 rounded-lg glass-card flex items-center justify-center hover:glow-border transition-all">
                      <ThumbsDown className="w-3.5 h-3.5 text-muted-foreground" />
                    </button>
                  </TooltipTrigger>
                  <TooltipContent className="glass-card">Bad response</TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </div>
          )}
        </div>

        {message.quiz && (
          <QuizCard quiz={message.quiz} onAnswer={onAnswerQuiz} />
        )}
      </div>
    </div>
  )
}

function FormattedContent({ content }: { content: string }) {
  // Simple code block detection
  const parts = content.split(/(```[\s\S]*?```)/g)

  return (
    <>
      {parts.map((part, index) => {
        if (part.startsWith("```")) {
          const code = part.slice(3, -3).trim()
          const lines = code.split("\n")
          const language = lines[0]?.match(/^[a-z]+$/i) ? lines.shift() : ""
          return (
            <div key={index} className="my-3 -mx-1">
              <div className="code-block rounded-xl overflow-hidden">
                {language && (
                  <div className="px-4 py-2 border-b border-border/30 flex items-center justify-between">
                    <span className="text-xs text-muted-foreground uppercase tracking-wider">{language}</span>
                    <button className="text-xs text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1">
                      <Copy className="w-3 h-3" />
                      Copy
                    </button>
                  </div>
                )}
                <pre className="p-4 overflow-x-auto">
                  <code className="text-xs font-mono text-foreground">{lines.join("\n")}</code>
                </pre>
              </div>
            </div>
          )
        }
        return <span key={index} className="whitespace-pre-wrap">{part}</span>
      })}
    </>
  )
}

function TypingIndicator() {
  return (
    <div className="flex gap-4 message-animate-in">
      <div className="w-10 h-10 rounded-xl gradient-primary glow-soft flex items-center justify-center">
        <Bot className="w-5 h-5 text-primary-foreground" />
      </div>
      <div className="glass-card rounded-2xl px-5 py-4 flex items-center gap-2">
        <div className="flex gap-1">
          <span className="w-2 h-2 rounded-full bg-primary animate-bounce" style={{ animationDelay: "0ms" }} />
          <span className="w-2 h-2 rounded-full bg-primary animate-bounce" style={{ animationDelay: "150ms" }} />
          <span className="w-2 h-2 rounded-full bg-primary animate-bounce" style={{ animationDelay: "300ms" }} />
        </div>
        <span className="text-xs text-muted-foreground ml-2">Thinking...</span>
      </div>
    </div>
  )
}
