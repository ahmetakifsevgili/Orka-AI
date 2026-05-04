using System;
using System.Collections.Generic;

namespace Orka.Core.DTOs;

public record AdaptiveLearningContext(
    Guid? TopicId,
    string TopicTitle,
    string UserLevel,
    List<WeakSkillEntry> WeakSkills,
    List<WeakConceptEntry> WeakConcepts,
    List<MistakePatternEntry> MistakePatterns,
    RecentQuizStats? QuizSummary,
    List<string> DueReviewSkills,
    string? StudentProfileSummary,
    string? PreviousSessionSummary,
    DateTime SnapshotAt
);

public record WeakSkillEntry(string Skill, string? TopicPath, int WrongCount, int TotalCount, double Accuracy);
public record WeakConceptEntry(string Concept, int Frequency);
public record MistakePatternEntry(string Category, int Frequency, string Label);
public record RecentQuizStats(double AverageAccuracy, int TotalAttempts);
