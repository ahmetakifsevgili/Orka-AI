/*
 * Design: "Sessiz Lüks" — Premium landing page.
 * Inspired by Linear.app hero, Vercel depth, Stripe premium feel.
 * Monochrome zinc palette. Typography-driven hierarchy.
 * Sections: Hero, Features, How It Works, CTA.
 */

import { Link } from "wouter";
import { motion } from "framer-motion";
import { ArrowRight, BookOpen, Brain, Target, Layers, Zap, Shield } from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";

const HERO_BG = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/hero-abstract-CJpjj4mLRnNXJor3rRcL7z.webp";
const FEATURE_1 = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/landing-feature-1-Sf6RHeHu3cSRac9MjeHesk.webp";
const FEATURE_2 = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/landing-feature-2-3qxTjA6Xr9goC7xUTxTr4Z.webp";
const FEATURE_3 = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/landing-feature-3-KXEMA63jg54Bu92y5UBssW.webp";

const fadeUp = {
  initial: { opacity: 0, y: 20 },
  whileInView: { opacity: 1, y: 0 },
  viewport: { once: true, margin: "-50px" },
  transition: { duration: 0.5, ease: "easeOut" as const },
};

const stagger = {
  initial: { opacity: 0, y: 20 },
  whileInView: { opacity: 1, y: 0 },
  viewport: { once: true },
} as const;

export default function Landing() {
  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 overflow-x-hidden">
      {/* Navigation */}
      <nav className="fixed top-0 left-0 right-0 z-50 bg-zinc-950/80 backdrop-blur-sm border-b border-zinc-800/50">
        <div className="max-w-6xl mx-auto px-6 h-14 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <OrcaLogo className="w-5 h-5 text-zinc-100" />
            <span className="font-semibold text-sm text-zinc-100">Orka AI</span>
          </div>
          <div className="flex items-center gap-6">
            <a href="#features" className="text-xs text-zinc-500 hover:text-zinc-300 transition-colors duration-150">
              Features
            </a>
            <a href="#how-it-works" className="text-xs text-zinc-500 hover:text-zinc-300 transition-colors duration-150">
              How It Works
            </a>
            <Link
              href="/login"
              className="text-xs font-medium text-zinc-950 bg-zinc-100 hover:bg-zinc-200 px-4 py-2 rounded-lg transition-colors duration-150"
            >
              Giriş Yap
            </Link>
          </div>
        </div>
      </nav>

      {/* Hero Section */}
      <section className="relative pt-32 pb-20 px-6">
        {/* Background Image */}
        <div className="absolute inset-0 overflow-hidden">
          <img
            src={HERO_BG}
            alt=""
            className="w-full h-full object-cover opacity-30"
          />
          <div className="absolute inset-0 bg-gradient-to-b from-zinc-950/60 via-zinc-950/80 to-zinc-950" />
        </div>

        <div className="relative max-w-4xl mx-auto text-center">
          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, ease: "easeOut" }}
          >
            {/* Badge */}
            <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 mb-8">
              <div className="w-1.5 h-1.5 rounded-full bg-zinc-400 animate-pulse" />
              <span className="text-[11px] text-zinc-400">AI-Powered Learning Ecosystem</span>
            </div>

            <h1 className="text-5xl sm:text-6xl font-bold tracking-tight text-zinc-50 leading-[1.1] mb-6">
              Master any subject
              <br />
              <span className="text-zinc-500">with intelligent guidance</span>
            </h1>

            <p className="text-lg text-zinc-500 max-w-2xl mx-auto mb-10 leading-relaxed">
              Orka AI builds a persistent knowledge graph as you learn. Structured curricula,
              interactive quizzes, and a living wiki — all driven by AI that adapts to you.
            </p>

            <div className="flex items-center justify-center gap-4">
              <Link
                href="/login"
                className="inline-flex items-center gap-2 px-6 py-3 bg-zinc-100 text-zinc-950 rounded-lg font-medium text-sm hover:bg-zinc-200 transition-colors duration-150"
              >
                Start Learning
                <ArrowRight className="w-4 h-4" />
              </Link>
              <a
                href="#features"
                className="inline-flex items-center gap-2 px-6 py-3 border border-zinc-800 text-zinc-300 rounded-lg text-sm hover:bg-zinc-900 hover:border-zinc-700 transition-colors duration-150"
              >
                See How It Works
              </a>
            </div>
          </motion.div>

          {/* Product Preview */}
          <motion.div
            initial={{ opacity: 0, y: 40 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, delay: 0.3, ease: "easeOut" }}
            className="mt-16 relative"
          >
            <div className="rounded-xl border border-zinc-800 overflow-hidden shadow-2xl shadow-zinc-950/50">
              <img
                src={FEATURE_1}
                alt="Orka AI Interface"
                className="w-full"
              />
            </div>
            {/* Subtle glow under the image */}
            <div className="absolute -bottom-8 left-1/2 -translate-x-1/2 w-3/4 h-16 bg-zinc-800/20 blur-3xl rounded-full" />
          </motion.div>
        </div>
      </section>

      {/* Stats Bar */}
      <section className="border-y border-zinc-800/50 py-12 px-6">
        <div className="max-w-4xl mx-auto grid grid-cols-3 gap-8">
          {[
            { value: "10K+", label: "Active Learners" },
            { value: "500+", label: "Topics Available" },
            { value: "95%", label: "Completion Rate" },
          ].map((stat) => (
            <motion.div key={stat.label} {...fadeUp} className="text-center">
              <p className="text-3xl font-bold text-zinc-100">{stat.value}</p>
              <p className="text-xs text-zinc-500 mt-1">{stat.label}</p>
            </motion.div>
          ))}
        </div>
      </section>

      {/* Features Section */}
      <section id="features" className="py-24 px-6">
        <div className="max-w-5xl mx-auto">
          <motion.div {...fadeUp} className="text-center mb-16">
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
              Features
            </p>
            <h2 className="text-3xl sm:text-4xl font-bold text-zinc-100 mb-4">
              Everything you need to learn effectively
            </h2>
            <p className="text-sm text-zinc-500 max-w-lg mx-auto">
              A complete learning command center that combines AI tutoring,
              structured curricula, and knowledge management.
            </p>
          </motion.div>

          {/* Feature Grid */}
          <div className="grid grid-cols-3 gap-4 mb-16">
            {[
              {
                icon: Brain,
                title: "AI-Powered Tutoring",
                description: "Conversational AI that explains concepts with depth, using markdown-formatted responses with code examples and tables.",
              },
              {
                icon: Layers,
                title: "Structured Curricula",
                description: "Use /plan to auto-generate learning paths. The AI creates topic hierarchies with progressive sub-lessons.",
              },
              {
                icon: Target,
                title: "Interactive Quizzes",
                description: "Academic-style multiple-choice quizzes appear naturally during learning. Track your accuracy and review history.",
              },
              {
                icon: BookOpen,
                title: "Living Knowledge Wiki",
                description: "Every sub-lesson has a wiki page with crystallized notes, key points, and code examples that grow as you learn.",
              },
              {
                icon: Zap,
                title: "Persistent Knowledge Graph",
                description: "Your learning progress is mapped in a sidebar knowledge tree. See what you've mastered and what's next.",
              },
              {
                icon: Shield,
                title: "Personal Notes",
                description: "Add your own notes to any wiki page. Build a personal knowledge base alongside the AI-generated content.",
              },
            ].map((feature, i) => (
              <motion.div
                key={feature.title}
                {...stagger}
                transition={{ duration: 0.4, delay: i * 0.08 }}
                className="p-5 rounded-lg border border-zinc-800 bg-zinc-900/30 hover:bg-zinc-900/50 transition-colors duration-200"
              >
                <feature.icon className="w-5 h-5 text-zinc-500 mb-3" />
                <h3 className="text-sm font-medium text-zinc-200 mb-2">{feature.title}</h3>
                <p className="text-xs text-zinc-500 leading-relaxed">{feature.description}</p>
              </motion.div>
            ))}
          </div>

          {/* Feature Showcase 1 */}
          <motion.div {...fadeUp} className="grid grid-cols-2 gap-8 items-center mb-24">
            <div>
              <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
                Intelligent Quizzes
              </p>
              <h3 className="text-2xl font-bold text-zinc-100 mb-4">
                Test your knowledge as you learn
              </h3>
              <p className="text-sm text-zinc-500 leading-relaxed mb-4">
                Quizzes appear naturally during your learning sessions. The AI adapts question
                difficulty based on your progress and identifies areas that need reinforcement.
              </p>
              <ul className="space-y-2">
                {["Academic multiple-choice format", "Instant feedback with explanations", "Progress tracking and history"].map((item) => (
                  <li key={item} className="flex items-center gap-2 text-xs text-zinc-400">
                    <div className="w-1 h-1 rounded-full bg-zinc-600" />
                    {item}
                  </li>
                ))}
              </ul>
            </div>
            <div className="rounded-xl border border-zinc-800 overflow-hidden">
              <img src={FEATURE_2} alt="Quiz Interface" className="w-full" />
            </div>
          </motion.div>

          {/* Feature Showcase 2 */}
          <motion.div {...fadeUp} className="grid grid-cols-2 gap-8 items-center">
            <div className="rounded-xl border border-zinc-800 overflow-hidden order-1">
              <img src={FEATURE_3} alt="Wiki Interface" className="w-full" />
            </div>
            <div className="order-2">
              <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
                Knowledge Wiki
              </p>
              <h3 className="text-2xl font-bold text-zinc-100 mb-4">
                Your personal knowledge base
              </h3>
              <p className="text-sm text-zinc-500 leading-relaxed mb-4">
                Every topic you learn gets its own wiki page with structured content,
                code examples, comparison tables, and key takeaways. Add personal notes
                to build your own reference library.
              </p>
              <ul className="space-y-2">
                {["Rich markdown content with code blocks", "Key points summary for quick review", "Personal notes for each lesson"].map((item) => (
                  <li key={item} className="flex items-center gap-2 text-xs text-zinc-400">
                    <div className="w-1 h-1 rounded-full bg-zinc-600" />
                    {item}
                  </li>
                ))}
              </ul>
            </div>
          </motion.div>
        </div>
      </section>

      {/* How It Works */}
      <section id="how-it-works" className="py-24 px-6 border-t border-zinc-800/50">
        <div className="max-w-4xl mx-auto">
          <motion.div {...fadeUp} className="text-center mb-16">
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
              How It Works
            </p>
            <h2 className="text-3xl sm:text-4xl font-bold text-zinc-100 mb-4">
              Three steps to mastery
            </h2>
          </motion.div>

          <div className="grid grid-cols-3 gap-6">
            {[
              {
                step: "01",
                title: "Choose a Topic",
                description: "Ask the AI anything or use /plan to create a structured learning path with progressive sub-lessons.",
              },
              {
                step: "02",
                title: "Learn & Practice",
                description: "Engage with AI-powered explanations, take quizzes, and build your knowledge wiki as you progress.",
              },
              {
                step: "03",
                title: "Track & Master",
                description: "Monitor your progress through the knowledge map, review quiz history, and achieve mastery badges.",
              },
            ].map((item, i) => (
              <motion.div
                key={item.step}
                {...stagger}
                transition={{ duration: 0.4, delay: i * 0.1 }}
                className="relative"
              >
                <span className="text-5xl font-bold text-zinc-800/50 mb-4 block">{item.step}</span>
                <h3 className="text-sm font-medium text-zinc-200 mb-2">{item.title}</h3>
                <p className="text-xs text-zinc-500 leading-relaxed">{item.description}</p>
              </motion.div>
            ))}
          </div>
        </div>
      </section>

      {/* Video Section */}
      <section id="video" className="py-24 px-6 border-t border-zinc-800/50">
        <div className="max-w-4xl mx-auto text-center">
          <motion.div {...fadeUp}>
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
              See It In Action
            </p>
            <h2 className="text-3xl sm:text-4xl font-bold text-zinc-100 mb-8">
              Watch how Orka AI transforms learning
            </h2>
            <div className="rounded-xl border border-zinc-800 overflow-hidden bg-zinc-900 shadow-2xl shadow-zinc-950/50">
              <video
                className="w-full aspect-video bg-zinc-950"
                controls
                autoPlay
                muted
                loop
                playsInline
                poster="https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/video-keyframe-1-LvjTUMtbNfbZK9DZfshLfh.webp"
                preload="auto"
                src="https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/orka-promo_30c32572.mp4"
              >
                <source src="https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/orka-promo_30c32572.mp4" type="video/mp4" />
                Your browser does not support the video tag.
              </video>
            </div>
          </motion.div>
        </div>
      </section>

      {/* CTA Section */}
      <section className="py-24 px-6 border-t border-zinc-800/50">
        <div className="max-w-3xl mx-auto text-center">
          <motion.div {...fadeUp}>
            <h2 className="text-3xl sm:text-4xl font-bold text-zinc-100 mb-4">
              Ready to start learning?
            </h2>
            <p className="text-sm text-zinc-500 mb-8 max-w-md mx-auto">
              Join thousands of learners who use Orka AI to master technical subjects
              faster and more effectively.
            </p>
            <Link
              href="/login"
              className="inline-flex items-center gap-2 px-8 py-3.5 bg-zinc-100 text-zinc-950 rounded-lg font-medium text-sm hover:bg-zinc-200 transition-colors duration-150"
            >
              Launch Orka AI
              <ArrowRight className="w-4 h-4" />
            </Link>
          </motion.div>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-zinc-800/50 py-8 px-6">
        <div className="max-w-6xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-2">
            <OrcaLogo className="w-4 h-4 text-zinc-600" />
            <span className="text-xs text-zinc-600">Orka AI</span>
          </div>
          <p className="text-[10px] text-zinc-700">
            Built with intelligence. Designed for mastery.
          </p>
        </div>
      </footer>
    </div>
  );
}
