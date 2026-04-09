"use client"

import { useState, useEffect } from "react"
import { CheckCircle2, XCircle, HelpCircle, Lightbulb, RotateCcw, Sparkles, Trophy, ArrowRight } from "lucide-react"
import { cn } from "@/lib/utils"
import type { QuizQuestion } from "@/lib/types"
import { Button } from "@/components/ui/button"
import confetti from "canvas-confetti"

interface QuizCardProps {
  quiz: QuizQuestion
  onAnswer: (selectedIndex: number) => void
}

export function QuizCard({ quiz, onAnswer }: QuizCardProps) {
  const hasAnswered = quiz.selectedIndex !== undefined
  const isCorrect = hasAnswered && quiz.selectedIndex === quiz.correctIndex
  const [showHint, setShowHint] = useState(false)
  const [hoveredOption, setHoveredOption] = useState<number | null>(null)

  // Confetti effect on correct answer
  useEffect(() => {
    if (isCorrect) {
      confetti({
        particleCount: 100,
        spread: 70,
        origin: { y: 0.6 },
        colors: ["#06B6D4", "#14B8A6", "#22D3EE", "#10B981"],
      })
    }
  }, [isCorrect])

  return (
    <div className="w-full max-w-[90%] glass-card rounded-2xl overflow-hidden shadow-xl">
      {/* Header */}
      <div className="flex items-center justify-between px-5 py-4 border-b border-border/30 bg-gradient-to-r from-primary/10 to-chart-3/10">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl gradient-primary flex items-center justify-center glow-soft">
            <Sparkles className="w-5 h-5 text-primary-foreground" />
          </div>
          <div>
            <span className="text-sm font-semibold text-foreground">Quick Quiz</span>
            <p className="text-[10px] text-muted-foreground uppercase tracking-wider">Test your knowledge</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {!hasAnswered && (
            <button
              onClick={() => setShowHint(!showHint)}
              className={cn(
                "text-xs flex items-center gap-1.5 px-3 py-1.5 rounded-full transition-all",
                showHint
                  ? "glass-card glow-border text-chart-4"
                  : "glass-card text-muted-foreground hover:text-foreground"
              )}
            >
              <Lightbulb className="w-3.5 h-3.5" />
              Hint
            </button>
          )}
          {hasAnswered && (
            <div
              className={cn(
                "flex items-center gap-2 px-4 py-2 rounded-full",
                isCorrect
                  ? "bg-chart-3/20 text-chart-3"
                  : "bg-destructive/20 text-destructive"
              )}
            >
              {isCorrect ? (
                <>
                  <Trophy className="w-4 h-4" />
                  <span className="text-sm font-semibold">Correct!</span>
                </>
              ) : (
                <>
                  <XCircle className="w-4 h-4" />
                  <span className="text-sm font-semibold">Incorrect</span>
                </>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Question */}
      <div className="p-5">
        <p className="text-base text-foreground font-medium leading-relaxed">{quiz.question}</p>

        {/* Hint */}
        {showHint && !hasAnswered && (
          <div className="mt-4 p-4 rounded-xl glass-card border border-chart-4/30 flex items-start gap-3">
            <div className="w-8 h-8 rounded-lg bg-chart-4/20 flex items-center justify-center flex-shrink-0">
              <Lightbulb className="w-4 h-4 text-chart-4" />
            </div>
            <p className="text-sm text-muted-foreground leading-relaxed">
              Think about what you need when creating a new instance of a type...
            </p>
          </div>
        )}

        {/* Options */}
        <div className="space-y-3 mt-5">
          {quiz.options.map((option, index) => {
            const isSelected = quiz.selectedIndex === index
            const isCorrectOption = quiz.correctIndex === index
            const showCorrect = hasAnswered && isCorrectOption
            const showIncorrect = hasAnswered && isSelected && !isCorrectOption

            return (
              <button
                key={index}
                onClick={() => !hasAnswered && onAnswer(index)}
                disabled={hasAnswered}
                onMouseEnter={() => !hasAnswered && setHoveredOption(index)}
                onMouseLeave={() => setHoveredOption(null)}
                className={cn(
                  "w-full flex items-center gap-4 px-4 py-4 rounded-xl text-left text-sm transition-all duration-200",
                  hasAnswered
                    ? showCorrect
                      ? "glass-card border-2 border-chart-3 bg-chart-3/10 glow-border"
                      : showIncorrect
                      ? "glass-card border-2 border-destructive bg-destructive/10"
                      : "glass-card opacity-50"
                    : "glass-card hover:glow-border cursor-pointer",
                  !hasAnswered && hoveredOption === index && "scale-[1.02]"
                )}
                style={{ 
                  animationDelay: `${index * 100}ms`,
                  boxShadow: showCorrect ? '0 0 20px oklch(0.75 0.15 145 / 0.3)' : undefined
                }}
              >
                <span
                  className={cn(
                    "w-10 h-10 rounded-xl flex items-center justify-center text-sm font-bold flex-shrink-0 transition-all",
                    hasAnswered
                      ? showCorrect
                        ? "bg-chart-3 text-primary-foreground"
                        : showIncorrect
                        ? "bg-destructive text-destructive-foreground"
                        : "bg-muted text-muted-foreground"
                      : hoveredOption === index
                        ? "gradient-primary text-primary-foreground"
                        : "bg-secondary/80 text-muted-foreground"
                  )}
                >
                  {hasAnswered && showCorrect ? (
                    <CheckCircle2 className="w-5 h-5" />
                  ) : hasAnswered && showIncorrect ? (
                    <XCircle className="w-5 h-5" />
                  ) : (
                    String.fromCharCode(65 + index)
                  )}
                </span>
                <span className={cn(
                  "flex-1 font-medium",
                  showCorrect && "text-chart-3",
                  showIncorrect && "text-destructive"
                )}>
                  {option}
                </span>
                {!hasAnswered && hoveredOption === index && (
                  <ArrowRight className="w-4 h-4 text-primary" />
                )}
              </button>
            )
          })}
        </div>

        {/* Result message */}
        {hasAnswered && (
          <div
            className={cn(
              "mt-5 p-4 rounded-xl flex items-start gap-3",
              isCorrect ? "glass-card border border-chart-3/30" : "glass-card"
            )}
          >
            <div className={cn(
              "w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0",
              isCorrect ? "bg-chart-3/20" : "bg-muted"
            )}>
              {isCorrect ? (
                <Trophy className="w-5 h-5 text-chart-3" />
              ) : (
                <HelpCircle className="w-5 h-5 text-muted-foreground" />
              )}
            </div>
            <div className="flex-1">
              {isCorrect ? (
                <>
                  <p className="text-sm font-semibold text-chart-3 mb-1">Excellent work!</p>
                  <p className="text-xs text-muted-foreground">
                    You&apos;ve got a solid understanding of this concept.
                  </p>
                </>
              ) : (
                <>
                  <p className="text-sm font-medium text-foreground mb-1">
                    The correct answer was:
                  </p>
                  <p className="text-sm text-primary font-semibold">
                    {quiz.options[quiz.correctIndex]}
                  </p>
                </>
              )}
            </div>
          </div>
        )}

        {/* Actions */}
        {hasAnswered && (
          <div className="mt-5 flex items-center gap-3">
            <Button 
              variant="outline" 
              size="sm" 
              className="flex-1 glass-card border-border/50 hover:glow-border"
            >
              <RotateCcw className="w-3.5 h-3.5 mr-2" />
              Try Another
            </Button>
            <Button 
              size="sm" 
              className="flex-1 gradient-primary hover:glow-soft"
            >
              Continue Learning
              <ArrowRight className="w-3.5 h-3.5 ml-2" />
            </Button>
          </div>
        )}
      </div>
    </div>
  )
}
