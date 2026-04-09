"use client"

import { useState } from "react"
import {
  Search,
  Settings,
  Moon,
  Sun,
  Bell,
  Keyboard,
  HelpCircle,
  LogOut,
  User,
  Zap,
  Target,
  BookOpen,
  ChevronDown,
  Command,
  Sparkles,
  Flame,
} from "lucide-react"
import { cn } from "@/lib/utils"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Kbd } from "@/components/ui/kbd"

interface HeaderProps {
  onOpenSearch: () => void
  onOpenSettings: () => void
  stats: {
    lessonsCompleted: number
    quizzesPassed: number
    streak: number
  }
}

export function Header({ onOpenSearch, onOpenSettings, stats }: HeaderProps) {
  const [showShortcuts, setShowShortcuts] = useState(false)
  const [isDark, setIsDark] = useState(true)

  return (
    <>
      <header className="h-16 glass-subtle border-b border-border/50 flex items-center justify-between px-4 lg:px-6 gap-4 relative z-20">
        {/* Left section - Logo */}
        <div className="flex items-center gap-3">
          <div className="relative">
            <div className="w-10 h-10 rounded-xl gradient-primary flex items-center justify-center glow-soft">
              <Sparkles className="w-5 h-5 text-primary-foreground" />
            </div>
            {/* Online indicator */}
            <div className="absolute -bottom-0.5 -right-0.5 w-3 h-3 rounded-full bg-chart-3 border-2 border-background" />
          </div>
          <div className="hidden sm:block">
            <h1 className="text-lg font-semibold text-foreground tracking-tight">Orka AI</h1>
            <p className="text-[10px] text-muted-foreground uppercase tracking-widest">Learning Assistant</p>
          </div>
        </div>

        {/* Center section - Search */}
        <button
          onClick={onOpenSearch}
          className="flex-1 max-w-xl flex items-center gap-3 px-4 py-2.5 rounded-xl glass-card hover:glow-border transition-all group"
        >
          <Search className="w-4 h-4 text-muted-foreground group-hover:text-primary transition-colors" />
          <span className="text-sm text-muted-foreground flex-1 text-left hidden sm:block">
            Search topics, lessons, or commands...
          </span>
          <span className="text-sm text-muted-foreground sm:hidden">Search...</span>
          <div className="hidden md:flex items-center gap-1 px-2 py-1 rounded-lg bg-muted/50">
            <Kbd className="text-[10px] px-1.5 py-0.5">
              <Command className="w-2.5 h-2.5" />
            </Kbd>
            <Kbd className="text-[10px] px-1.5 py-0.5">K</Kbd>
          </div>
        </button>

        {/* Right section - Actions */}
        <div className="flex items-center gap-2">
          {/* Stats - Glass pills */}
          <div className="hidden lg:flex items-center gap-2 mr-2 pr-4 border-r border-border/50">
            <StatPill icon={BookOpen} value={stats.lessonsCompleted} label="lessons" color="text-primary" />
            <StatPill icon={Target} value={stats.quizzesPassed} label="quizzes" color="text-chart-3" />
            <StatPill icon={Flame} value={stats.streak} label="streak" color="text-chart-4" isStreak />
          </div>

          {/* Action buttons */}
          <ActionButton onClick={() => setShowShortcuts(true)} tooltip="Keyboard shortcuts">
            <Keyboard className="w-4 h-4" />
          </ActionButton>

          <ActionButton tooltip="Notifications" badge>
            <Bell className="w-4 h-4" />
          </ActionButton>

          <ActionButton onClick={() => setIsDark(!isDark)} tooltip={isDark ? "Light mode" : "Dark mode"}>
            {isDark ? <Moon className="w-4 h-4" /> : <Sun className="w-4 h-4" />}
          </ActionButton>

          <ActionButton onClick={onOpenSettings} tooltip="Settings">
            <Settings className="w-4 h-4" />
          </ActionButton>

          {/* User menu */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button className="flex items-center gap-2 ml-2 pl-3 border-l border-border/50 focus-ring rounded-lg py-1">
                <div className="relative">
                  <Avatar className="w-9 h-9 ring-2 ring-primary/20">
                    <AvatarImage src="/placeholder-user.jpg" alt="User" />
                    <AvatarFallback className="gradient-primary text-primary-foreground text-sm font-medium">
                      OK
                    </AvatarFallback>
                  </Avatar>
                  {/* Level badge */}
                  <div className="absolute -bottom-1 -right-1 w-5 h-5 rounded-full bg-chart-4 text-[10px] font-bold text-primary-foreground flex items-center justify-center border-2 border-background">
                    5
                  </div>
                </div>
                <ChevronDown className="w-3.5 h-3.5 text-muted-foreground hidden sm:block" />
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-64 glass-card border-border/50">
              <DropdownMenuLabel className="font-normal p-4">
                <div className="flex items-center gap-3">
                  <Avatar className="w-12 h-12">
                    <AvatarFallback className="gradient-primary text-primary-foreground text-lg font-medium">
                      OK
                    </AvatarFallback>
                  </Avatar>
                  <div className="flex-1">
                    <p className="text-sm font-semibold text-foreground">Orka User</p>
                    <p className="text-xs text-muted-foreground">user@orka.ai</p>
                    <div className="flex items-center gap-1.5 mt-1">
                      <div className="px-2 py-0.5 rounded-full bg-primary/20 text-[10px] text-primary font-medium">
                        Level 5
                      </div>
                      <div className="px-2 py-0.5 rounded-full bg-chart-4/20 text-[10px] text-chart-4 font-medium flex items-center gap-1">
                        <Flame className="w-2.5 h-2.5" />
                        {stats.streak} days
                      </div>
                    </div>
                  </div>
                </div>
              </DropdownMenuLabel>
              <DropdownMenuSeparator className="bg-border/50" />
              <DropdownMenuItem className="py-2.5 cursor-pointer">
                <User className="w-4 h-4 mr-3 text-muted-foreground" />
                Profile
              </DropdownMenuItem>
              <DropdownMenuItem onClick={onOpenSettings} className="py-2.5 cursor-pointer">
                <Settings className="w-4 h-4 mr-3 text-muted-foreground" />
                Settings
              </DropdownMenuItem>
              <DropdownMenuItem className="py-2.5 cursor-pointer">
                <HelpCircle className="w-4 h-4 mr-3 text-muted-foreground" />
                Help & Support
              </DropdownMenuItem>
              <DropdownMenuSeparator className="bg-border/50" />
              <DropdownMenuItem className="py-2.5 text-destructive focus:text-destructive cursor-pointer">
                <LogOut className="w-4 h-4 mr-3" />
                Log out
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </header>

      {/* Keyboard Shortcuts Dialog */}
      <Dialog open={showShortcuts} onOpenChange={setShowShortcuts}>
        <DialogContent className="sm:max-w-md glass-card border-border/50">
          <DialogHeader>
            <DialogTitle className="text-xl font-semibold">Keyboard Shortcuts</DialogTitle>
            <DialogDescription className="text-muted-foreground">
              Navigate faster with these shortcuts
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-6 mt-4">
            <ShortcutGroup title="Navigation">
              <ShortcutItem keys={["Cmd", "K"]} description="Open search" />
              <ShortcutItem keys={["Cmd", "/"]} description="Focus chat input" />
              <ShortcutItem keys={["Cmd", "B"]} description="Toggle sidebar" />
              <ShortcutItem keys={["Esc"]} description="Close panel/dialog" />
            </ShortcutGroup>
            <ShortcutGroup title="Chat">
              <ShortcutItem keys={["Enter"]} description="Send message" />
              <ShortcutItem keys={["Shift", "Enter"]} description="New line" />
              <ShortcutItem keys={["Cmd", "Enter"]} description="Send with quiz request" />
            </ShortcutGroup>
            <ShortcutGroup title="Learning">
              <ShortcutItem keys={["1-4"]} description="Select quiz answer" />
              <ShortcutItem keys={["N"]} description="Next lesson" />
              <ShortcutItem keys={["P"]} description="Previous lesson" />
            </ShortcutGroup>
          </div>
        </DialogContent>
      </Dialog>
    </>
  )
}

interface StatPillProps {
  icon: React.ElementType
  value: number
  label: string
  color: string
  isStreak?: boolean
}

function StatPill({ icon: Icon, value, label, color, isStreak }: StatPillProps) {
  return (
    <div className={cn(
      "flex items-center gap-2 px-3 py-1.5 rounded-full glass-card",
      isStreak && "pulse-glow"
    )}>
      <Icon className={cn("w-4 h-4", color)} />
      <span className="text-sm font-medium text-foreground">{value}</span>
      <span className="text-xs text-muted-foreground hidden xl:block">{label}</span>
    </div>
  )
}

interface ActionButtonProps {
  children: React.ReactNode
  onClick?: () => void
  tooltip?: string
  badge?: boolean
}

function ActionButton({ children, onClick, badge }: ActionButtonProps) {
  return (
    <button
      onClick={onClick}
      className="relative w-9 h-9 rounded-xl flex items-center justify-center text-muted-foreground hover:text-foreground hover:bg-secondary/80 transition-all focus-ring"
    >
      {children}
      {badge && (
        <span className="absolute top-1.5 right-1.5 w-2 h-2 bg-primary rounded-full animate-pulse" />
      )}
    </button>
  )
}

function ShortcutGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <h4 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-3">{title}</h4>
      <div className="space-y-2">{children}</div>
    </div>
  )
}

function ShortcutItem({ keys, description }: { keys: string[]; description: string }) {
  return (
    <div className="flex items-center justify-between py-1.5">
      <span className="text-sm text-foreground">{description}</span>
      <div className="flex items-center gap-1">
        {keys.map((key, i) => (
          <Kbd key={i} className="px-2 py-1 text-xs">{key}</Kbd>
        ))}
      </div>
    </div>
  )
}
