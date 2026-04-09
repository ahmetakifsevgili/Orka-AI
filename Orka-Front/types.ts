export type MessageRole = "user" | "ai";
export type MessageType = "text" | "quiz" | "plan";

export interface QuizOption {
  id: string;
  text: string;
  isCorrect: boolean;
}

export interface QuizData {
  question: string;
  options: QuizOption[];
  explanation: string;
  topic?: string;
}

export interface ChatMessage {
  id: string;
  role: MessageRole;
  type: MessageType;
  content: string;
  quiz?: QuizData;
  timestamp: Date;
  topicId?: string;
}

export interface SubLesson {
  id: string;
  title: string;
  completed: boolean;
}

export interface Topic {
  id: string;
  title: string;
  icon: string;
  subLessons: SubLesson[];
  createdAt: Date;
}

export interface WikiContent {
  topicId: string;
  subLessonId: string;
  title: string;
  content: string;
  keyPoints: string[];
  lastUpdated: Date;
}

export interface WikiNote {
  id: string;
  subLessonId: string;
  content: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface QuizAttempt {
  id: string;
  messageId: string;
  question: string;
  selectedOptionId: string;
  isCorrect: boolean;
  explanation: string;
  timestamp: Date;
}

export interface AIResponse {
  content: string;
  type: MessageType;
  quiz?: QuizData;
  newTopic?: Topic;
}

export interface Conversation {
  id: string;
  title: string;
  lastMessage: string;
  timestamp: Date;
  messages: ChatMessage[];
}

export type CourseLevel = "Başlangıç" | "Orta" | "İleri";
export type CourseCategory = "Programlama" | "Veri Bilimi" | "Yapay Zeka" | "Web Geliştirme" | "Veritabanı";

export interface CourseModule {
  id: string;
  title: string;
  description: string;
  duration: string;
  lessons: CourseLesson[];
}

export interface CourseLesson {
  id: string;
  title: string;
  type: "video" | "article" | "quiz" | "exercise";
  duration: string;
  completed: boolean;
}

export interface Course {
  id: string;
  title: string;
  description: string;
  category: CourseCategory;
  level: CourseLevel;
  icon: string;
  totalModules: number;
  totalLessons: number;
  estimatedHours: number;
  progress: number; // 0-100
  enrolled: boolean;
  modules: CourseModule[];
  tags: string[];
  instructor: string;
  rating: number;
  students: number;
}
