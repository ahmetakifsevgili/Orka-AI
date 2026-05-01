using System;
using System.Collections.Generic;
using System.Linq;

namespace Orka.Core.Constants;

public static class LearningSignalTypes
{
    public const string QuizAnswered = "QuizAnswered";
    public const string WeaknessDetected = "WeaknessDetected";
    public const string SourceUploaded = "SourceUploaded";
    public const string SourceOpened = "SourceOpened";
    public const string SourceAsked = "SourceAsked";
    public const string WikiUpdated = "WikiUpdated";
    public const string WikiActionClicked = "WikiActionClicked";
    public const string ClassroomStarted = "ClassroomStarted";
    public const string ClassroomQuestionAsked = "ClassroomQuestionAsked";
    public const string IdeRunCompleted = "IdeRunCompleted";
    public const string IdeSentToTutor = "IdeSentToTutor";
    public const string RemediationStarted = "RemediationStarted";
    public const string RemediationCompleted = "RemediationCompleted";
    public const string LessonCompleted = "LessonCompleted";
    public const string YouTubeReferenceUsed = "YouTubeReferenceUsed";
    public const string NotebookSourceUsed = "NotebookSourceUsed";
    public const string MisconceptionDetected = "MisconceptionDetected";
    public const string TeachingMoveApplied = "TeachingMoveApplied";
    public const string SourceCitationMissing = "SourceCitationMissing";
    public const string LessonFocused = "LessonFocused";
    public const string WikiRailOpened = "WikiRailOpened";
    public const string ContextActionClicked = "ContextActionClicked";
    public const string SourceCitationOpened = "SourceCitationOpened";
    public const string RemedialActionStarted = "RemedialActionStarted";

    public static readonly IReadOnlyCollection<string> All =
    [
        QuizAnswered,
        WeaknessDetected,
        SourceUploaded,
        SourceOpened,
        SourceAsked,
        WikiUpdated,
        WikiActionClicked,
        ClassroomStarted,
        ClassroomQuestionAsked,
        IdeRunCompleted,
        IdeSentToTutor,
        RemediationStarted,
        RemediationCompleted,
        LessonCompleted,
        YouTubeReferenceUsed,
        NotebookSourceUsed,
        MisconceptionDetected,
        TeachingMoveApplied,
        SourceCitationMissing,
        LessonFocused,
        WikiRailOpened,
        ContextActionClicked,
        SourceCitationOpened,
        RemedialActionStarted
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var trimmed = value.Trim();
        return All.FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }
}
