export interface Lesson {
  id: string
  title: string
  completed: boolean
  notes?: string
  quizScore?: number
}

export interface Topic {
  id: string
  title: string
  lessons: Lesson[]
  expanded: boolean
}

export interface Message {
  id: string
  role: 'user' | 'assistant'
  content: string
  quiz?: QuizQuestion
  timestamp: Date
}

export interface QuizQuestion {
  id: string
  question: string
  options: string[]
  correctIndex: number
  selectedIndex?: number
}
