/*
 * Design: "Sessiz Lüks" — Full-screen profile dashboard.
 * Stats cards, GitHub-style heatmap, topic progress, achievements.
 * Sidebar persistent, content in center.
 */

import { useState } from "react";
import { Link } from "wouter";
import { ArrowLeft, Flame, BookOpen, Target, TrendingUp, Award, Calendar } from "lucide-react";
import { motion } from "framer-motion";
import OrcaLogo from "@/components/OrcaLogo";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";

const TABS = ["Overview", "Quiz History", "Analytics", "Goals"] as const;
type Tab = (typeof TABS)[number];

// Mock activity data for heatmap (52 weeks × 7 days)
function generateHeatmapData(): number[][] {
  const data: number[][] = [];
  for (let week = 0; week < 52; week++) {
    const weekData: number[] = [];
    for (let day = 0; day < 7; day++) {
      const rand = Math.random();
      if (rand > 0.7) weekData.push(Math.floor(Math.random() * 4) + 1);
      else if (rand > 0.4) weekData.push(1);
      else weekData.push(0);
    }
    data.push(weekData);
  }
  return data;
}

const heatmapData = generateHeatmapData();

const getHeatmapColor = (value: number): string => {
  if (value === 0) return "bg-zinc-800/50";
  if (value === 1) return "bg-zinc-700";
  if (value === 2) return "bg-zinc-600";
  if (value === 3) return "bg-zinc-500";
  return "bg-zinc-400";
};

const topicProgress = [
  { name: "Python Fundamentals", progress: 65, level: "Intermediate" },
  { name: "Machine Learning", progress: 35, level: "Beginner" },
];

const achievements = [
  { icon: "🔥", title: "7-Day Streak", description: "Studied 7 days in a row", rarity: "Common" },
  { icon: "🧠", title: "Quiz Master", description: "Scored 100% on 5 quizzes", rarity: "Rare" },
  { icon: "📚", title: "Knowledge Seeker", description: "Completed 10 sub-lessons", rarity: "Common" },
  { icon: "⚡", title: "Speed Learner", description: "Completed a topic in under 3 days", rarity: "Epic" },
  { icon: "🎯", title: "Perfect Score", description: "100% accuracy on first attempt", rarity: "Legendary" },
];

export default function Profile() {
  const [activeTab, setActiveTab] = useState<Tab>("Overview");
  const { attempts } = useQuizHistory();

  const correctAttempts = attempts.filter((a) => a.isCorrect).length;
  const accuracy = attempts.length > 0 ? Math.round((correctAttempts / attempts.length) * 100) : 87;

  const stats = [
    { icon: Flame, label: "Day Streak", value: "12", color: "text-zinc-300" },
    { icon: BookOpen, label: "Topics Mastered", value: "2", color: "text-zinc-300" },
    { icon: Target, label: "Quizzes Taken", value: String(attempts.length || 24), color: "text-zinc-300" },
    { icon: TrendingUp, label: "Accuracy Rate", value: `${accuracy}%`, color: "text-zinc-300" },
  ];

  return (
    <div className="h-screen flex overflow-hidden bg-zinc-950">
      {/* Minimal sidebar */}
      <div className="w-64 flex-shrink-0 bg-zinc-950 border-r border-zinc-800 flex flex-col">
        <div className="px-4 py-5">
          <Link href="/app" className="flex items-center gap-2.5">
            <OrcaLogo className="w-5 h-5 text-zinc-100" />
            <span className="font-semibold text-zinc-100 text-sm">Orka AI</span>
          </Link>
        </div>
        <div className="px-4 mt-4">
          <Link
            href="/app"
            className="flex items-center gap-2 text-xs text-zinc-500 hover:text-zinc-300 transition-colors duration-150"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            Back to Learning
          </Link>
        </div>

        {/* Tab navigation in sidebar */}
        <div className="px-2 mt-6">
          <p className="px-2 text-[10px] font-medium text-zinc-600 uppercase tracking-wider mb-2">
            Profile Sections
          </p>
          {TABS.map((tab) => (
            <button
              key={tab}
              onClick={() => setActiveTab(tab)}
              className={`w-full text-left px-3 py-2 rounded-md text-sm transition-colors duration-150 mb-0.5 ${
                activeTab === tab
                  ? "text-zinc-100 bg-zinc-800/50"
                  : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900/50"
              }`}
            >
              {tab}
            </button>
          ))}
        </div>
      </div>

      {/* Main content */}
      <div className="flex-1 overflow-y-auto bg-zinc-900">
        <div className="max-w-4xl mx-auto px-8 py-8">
          {/* Profile Header */}
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.25 }}
            className="flex items-center gap-4 mb-8"
          >
            <div className="w-14 h-14 rounded-full bg-zinc-800 border border-zinc-700 flex items-center justify-center">
              <span className="text-lg font-semibold text-zinc-300">OK</span>
            </div>
            <div>
              <h1 className="text-xl font-semibold text-zinc-100">Orka User</h1>
              <p className="text-sm text-zinc-500">Learning since January 2024</p>
            </div>
          </motion.div>

          {activeTab === "Overview" && (
            <OverviewTab stats={stats} />
          )}
          {activeTab === "Quiz History" && (
            <QuizHistoryTab attempts={attempts} />
          )}
          {activeTab === "Analytics" && (
            <AnalyticsTab />
          )}
          {activeTab === "Goals" && (
            <GoalsTab />
          )}
        </div>
      </div>
    </div>
  );
}

function OverviewTab({ stats }: { stats: { icon: React.ElementType; label: string; value: string; color: string }[] }) {
  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.2 }}
    >
      {/* Stats Grid */}
      <div className="grid grid-cols-4 gap-3 mb-8">
        {stats.map((stat) => (
          <div
            key={stat.label}
            className="bg-zinc-950 border border-zinc-800 rounded-lg p-4"
          >
            <stat.icon className="w-4 h-4 text-zinc-500 mb-2" />
            <p className="text-2xl font-semibold text-zinc-100">{stat.value}</p>
            <p className="text-xs text-zinc-500 mt-1">{stat.label}</p>
          </div>
        ))}
      </div>

      {/* Activity Heatmap */}
      <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-5 mb-8">
        <div className="flex items-center gap-2 mb-4">
          <Calendar className="w-4 h-4 text-zinc-500" />
          <h3 className="text-sm font-medium text-zinc-300">Learning Activity</h3>
        </div>
        <div className="flex gap-[3px] overflow-x-auto pb-2">
          {heatmapData.map((week, wi) => (
            <div key={wi} className="flex flex-col gap-[3px]">
              {week.map((day, di) => (
                <div
                  key={`${wi}-${di}`}
                  className={`w-[10px] h-[10px] rounded-[2px] ${getHeatmapColor(day)}`}
                  title={`${day} activities`}
                />
              ))}
            </div>
          ))}
        </div>
        <div className="flex items-center gap-2 mt-3">
          <span className="text-[10px] text-zinc-600">Less</span>
          {[0, 1, 2, 3, 4].map((v) => (
            <div key={v} className={`w-[10px] h-[10px] rounded-[2px] ${getHeatmapColor(v)}`} />
          ))}
          <span className="text-[10px] text-zinc-600">More</span>
        </div>
      </div>

      {/* Topic Progress */}
      <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-5 mb-8">
        <h3 className="text-sm font-medium text-zinc-300 mb-4">Topic Progress</h3>
        <div className="space-y-4">
          {topicProgress.map((topic) => (
            <div key={topic.name}>
              <div className="flex items-center justify-between mb-1.5">
                <span className="text-sm text-zinc-300">{topic.name}</span>
                <span className="text-xs text-zinc-500">{topic.level} · {topic.progress}%</span>
              </div>
              <div className="h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                <motion.div
                  initial={{ width: 0 }}
                  animate={{ width: `${topic.progress}%` }}
                  transition={{ duration: 0.6, ease: "easeOut" }}
                  className="h-full bg-zinc-500 rounded-full"
                />
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Achievements */}
      <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-5">
        <div className="flex items-center gap-2 mb-4">
          <Award className="w-4 h-4 text-zinc-500" />
          <h3 className="text-sm font-medium text-zinc-300">Achievements</h3>
        </div>
        <div className="grid grid-cols-2 gap-3">
          {achievements.map((ach) => (
            <div
              key={ach.title}
              className="flex items-start gap-3 p-3 rounded-lg border border-zinc-800 bg-zinc-900/50"
            >
              <span className="text-xl">{ach.icon}</span>
              <div>
                <p className="text-sm font-medium text-zinc-200">{ach.title}</p>
                <p className="text-xs text-zinc-500 mt-0.5">{ach.description}</p>
                <span className={`inline-block text-[10px] mt-1 px-1.5 py-0.5 rounded ${
                  ach.rarity === "Legendary" ? "bg-zinc-700 text-zinc-200" :
                  ach.rarity === "Epic" ? "bg-zinc-800 text-zinc-300" :
                  ach.rarity === "Rare" ? "bg-zinc-800 text-zinc-400" :
                  "bg-zinc-800/50 text-zinc-500"
                }`}>
                  {ach.rarity}
                </span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </motion.div>
  );
}

function QuizHistoryTab({ attempts }: { attempts: import("@/lib/types").QuizAttempt[] }) {
  if (attempts.length === 0) {
    return (
      <div className="text-center py-16">
        <Target className="w-8 h-8 text-zinc-700 mx-auto mb-3" />
        <p className="text-sm text-zinc-500">No quiz attempts yet.</p>
        <p className="text-xs text-zinc-600 mt-1">Complete quizzes in the chat to see your history here.</p>
      </div>
    );
  }

  return (
    <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.2 }}>
      <div className="space-y-3">
        {attempts.map((attempt) => (
          <div
            key={attempt.id}
            className={`p-4 rounded-lg border ${
              attempt.isCorrect
                ? "border-emerald-500/25 bg-emerald-500/10"
                : "border-amber-500/25 bg-amber-500/10"
            }`}
          >
            <div className="flex items-start justify-between">
              <p className="text-sm text-zinc-200 flex-1">{attempt.question}</p>
              <span className={`text-xs px-2 py-0.5 rounded ml-3 ${
                attempt.isCorrect ? "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300" : "bg-amber-500/10 text-amber-700 dark:text-amber-300"
              }`}>
                {attempt.isCorrect ? "Correct" : "Incorrect"}
              </span>
            </div>
            <p className="text-xs text-zinc-500 mt-2">{attempt.explanation}</p>
            <p className="text-[10px] text-zinc-600 mt-2">
              {attempt.timestamp.toLocaleString()}
            </p>
          </div>
        ))}
      </div>
    </motion.div>
  );
}

function AnalyticsTab() {
  const weeklyData = [3, 5, 2, 7, 4, 6, 8];
  const maxVal = Math.max(...weeklyData);
  const days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

  return (
    <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.2 }}>
      <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-5 mb-6">
        <h3 className="text-sm font-medium text-zinc-300 mb-4">Weekly Study Sessions</h3>
        <div className="flex items-end gap-3 h-32">
          {weeklyData.map((val, i) => (
            <div key={i} className="flex-1 flex flex-col items-center gap-1">
              <motion.div
                initial={{ height: 0 }}
                animate={{ height: `${(val / maxVal) * 100}%` }}
                transition={{ duration: 0.4, delay: i * 0.05 }}
                className="w-full bg-zinc-700 rounded-t"
              />
              <span className="text-[10px] text-zinc-600">{days[i]}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-5">
          <p className="text-xs text-zinc-500 mb-1">Avg. Session Duration</p>
          <p className="text-2xl font-semibold text-zinc-100">23 min</p>
        </div>
        <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-5">
          <p className="text-xs text-zinc-500 mb-1">Total Study Time</p>
          <p className="text-2xl font-semibold text-zinc-100">48 hrs</p>
        </div>
      </div>
    </motion.div>
  );
}

function GoalsTab() {
  const goals = [
    { title: "Complete Python Fundamentals", progress: 65, deadline: "Apr 30, 2024" },
    { title: "Pass ML Quiz with 90%+", progress: 35, deadline: "May 15, 2024" },
    { title: "Learn 3 New Topics", progress: 66, deadline: "Jun 1, 2024" },
  ];

  return (
    <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.2 }}>
      <div className="space-y-3">
        {goals.map((goal) => (
          <div key={goal.title} className="bg-zinc-950 border border-zinc-800 rounded-lg p-4">
            <div className="flex items-center justify-between mb-2">
              <p className="text-sm font-medium text-zinc-200">{goal.title}</p>
              <span className="text-xs text-zinc-500">{goal.progress}%</span>
            </div>
            <div className="h-1.5 bg-zinc-800 rounded-full overflow-hidden mb-2">
              <motion.div
                initial={{ width: 0 }}
                animate={{ width: `${goal.progress}%` }}
                transition={{ duration: 0.6, ease: "easeOut" }}
                className="h-full bg-zinc-500 rounded-full"
              />
            </div>
            <p className="text-[10px] text-zinc-600">Deadline: {goal.deadline}</p>
          </div>
        ))}
      </div>
    </motion.div>
  );
}
