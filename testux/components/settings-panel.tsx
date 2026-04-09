"use client"

import { useState } from "react"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { Slider } from "@/components/ui/slider"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Button } from "@/components/ui/button"
import { Separator } from "@/components/ui/separator"
import {
  Palette,
  Volume2,
  Brain,
  Bell,
  Shield,
  Download,
  Trash2,
} from "lucide-react"

interface SettingsPanelProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function SettingsPanel({ open, onOpenChange }: SettingsPanelProps) {
  const [settings, setSettings] = useState({
    theme: "dark",
    fontSize: 14,
    soundEffects: true,
    autoQuiz: true,
    quizDifficulty: "adaptive",
    notifications: true,
    emailDigest: false,
    dataCollection: true,
  })

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="w-full sm:max-w-lg overflow-y-auto">
        <SheetHeader>
          <SheetTitle>Settings</SheetTitle>
          <SheetDescription>
            Customize your learning experience
          </SheetDescription>
        </SheetHeader>

        <Tabs defaultValue="appearance" className="mt-6">
          <TabsList className="grid w-full grid-cols-4">
            <TabsTrigger value="appearance" className="text-xs">
              <Palette className="w-3 h-3 mr-1.5" />
              Look
            </TabsTrigger>
            <TabsTrigger value="learning" className="text-xs">
              <Brain className="w-3 h-3 mr-1.5" />
              Learn
            </TabsTrigger>
            <TabsTrigger value="notifications" className="text-xs">
              <Bell className="w-3 h-3 mr-1.5" />
              Notify
            </TabsTrigger>
            <TabsTrigger value="data" className="text-xs">
              <Shield className="w-3 h-3 mr-1.5" />
              Data
            </TabsTrigger>
          </TabsList>

          {/* Appearance Tab */}
          <TabsContent value="appearance" className="space-y-6 mt-6">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <Label className="text-sm font-medium">Theme</Label>
                  <p className="text-xs text-muted-foreground">
                    Choose your preferred color scheme
                  </p>
                </div>
                <Select
                  value={settings.theme}
                  onValueChange={(value) =>
                    setSettings({ ...settings, theme: value })
                  }
                >
                  <SelectTrigger className="w-32">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="light">Light</SelectItem>
                    <SelectItem value="dark">Dark</SelectItem>
                    <SelectItem value="system">System</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <Separator />

              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <Label className="text-sm font-medium">Font Size</Label>
                  <span className="text-sm text-muted-foreground">
                    {settings.fontSize}px
                  </span>
                </div>
                <Slider
                  value={[settings.fontSize]}
                  onValueChange={([value]) =>
                    setSettings({ ...settings, fontSize: value })
                  }
                  min={12}
                  max={20}
                  step={1}
                />
              </div>

              <Separator />

              <div className="flex items-center justify-between">
                <div>
                  <Label className="text-sm font-medium">Sound Effects</Label>
                  <p className="text-xs text-muted-foreground">
                    Play sounds for quiz answers and achievements
                  </p>
                </div>
                <Switch
                  checked={settings.soundEffects}
                  onCheckedChange={(checked) =>
                    setSettings({ ...settings, soundEffects: checked })
                  }
                />
              </div>
            </div>
          </TabsContent>

          {/* Learning Tab */}
          <TabsContent value="learning" className="space-y-6 mt-6">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <Label className="text-sm font-medium">Auto Quiz</Label>
                  <p className="text-xs text-muted-foreground">
                    Automatically include quizzes during lessons
                  </p>
                </div>
                <Switch
                  checked={settings.autoQuiz}
                  onCheckedChange={(checked) =>
                    setSettings({ ...settings, autoQuiz: checked })
                  }
                />
              </div>

              <Separator />

              <div className="flex items-center justify-between">
                <div>
                  <Label className="text-sm font-medium">Quiz Difficulty</Label>
                  <p className="text-xs text-muted-foreground">
                    Set the difficulty level for quizzes
                  </p>
                </div>
                <Select
                  value={settings.quizDifficulty}
                  onValueChange={(value) =>
                    setSettings({ ...settings, quizDifficulty: value })
                  }
                >
                  <SelectTrigger className="w-32">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="easy">Easy</SelectItem>
                    <SelectItem value="medium">Medium</SelectItem>
                    <SelectItem value="hard">Hard</SelectItem>
                    <SelectItem value="adaptive">Adaptive</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <Separator />

              <div className="p-4 rounded-lg bg-secondary/50 border border-border">
                <h4 className="text-sm font-medium mb-2">Learning Stats</h4>
                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <p className="text-muted-foreground">Total Lessons</p>
                    <p className="text-xl font-semibold">24</p>
                  </div>
                  <div>
                    <p className="text-muted-foreground">Avg. Quiz Score</p>
                    <p className="text-xl font-semibold">87%</p>
                  </div>
                  <div>
                    <p className="text-muted-foreground">Study Time</p>
                    <p className="text-xl font-semibold">12.5h</p>
                  </div>
                  <div>
                    <p className="text-muted-foreground">Current Streak</p>
                    <p className="text-xl font-semibold">5 days</p>
                  </div>
                </div>
              </div>
            </div>
          </TabsContent>

          {/* Notifications Tab */}
          <TabsContent value="notifications" className="space-y-6 mt-6">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <Label className="text-sm font-medium">Push Notifications</Label>
                  <p className="text-xs text-muted-foreground">
                    Receive reminders and updates
                  </p>
                </div>
                <Switch
                  checked={settings.notifications}
                  onCheckedChange={(checked) =>
                    setSettings({ ...settings, notifications: checked })
                  }
                />
              </div>

              <Separator />

              <div className="flex items-center justify-between">
                <div>
                  <Label className="text-sm font-medium">Weekly Email Digest</Label>
                  <p className="text-xs text-muted-foreground">
                    Summary of your learning progress
                  </p>
                </div>
                <Switch
                  checked={settings.emailDigest}
                  onCheckedChange={(checked) =>
                    setSettings({ ...settings, emailDigest: checked })
                  }
                />
              </div>
            </div>
          </TabsContent>

          {/* Data Tab */}
          <TabsContent value="data" className="space-y-6 mt-6">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <Label className="text-sm font-medium">Analytics</Label>
                  <p className="text-xs text-muted-foreground">
                    Help improve Orka with anonymous usage data
                  </p>
                </div>
                <Switch
                  checked={settings.dataCollection}
                  onCheckedChange={(checked) =>
                    setSettings({ ...settings, dataCollection: checked })
                  }
                />
              </div>

              <Separator />

              <div className="space-y-3">
                <h4 className="text-sm font-medium">Export & Delete</h4>
                <div className="flex gap-2">
                  <Button variant="secondary" size="sm" className="flex-1">
                    <Download className="w-4 h-4 mr-2" />
                    Export Data
                  </Button>
                  <Button variant="destructive" size="sm" className="flex-1">
                    <Trash2 className="w-4 h-4 mr-2" />
                    Delete Account
                  </Button>
                </div>
              </div>
            </div>
          </TabsContent>
        </Tabs>
      </SheetContent>
    </Sheet>
  )
}
