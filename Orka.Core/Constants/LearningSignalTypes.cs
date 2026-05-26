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
    public const string IdeCompileError = "IdeCompileError";
    public const string IdeRuntimeError = "IdeRuntimeError";
    public const string IdeExecutionTimeout = "IdeExecutionTimeout";
    public const string IdeProviderUnavailable = "IdeProviderUnavailable";
    public const string IdeTestFailure = "IdeTestFailure";
    public const string IdeBlankAttempt = "IdeBlankAttempt";
    public const string IdeSentToTutor = "IdeSentToTutor";
    public const string RemediationStarted = "RemediationStarted";
    public const string RemediationCompleted = "RemediationCompleted";
    public const string ReviewCompleted = "ReviewCompleted";
    public const string DailyChallengeAssigned = "DailyChallengeAssigned";
    public const string DailyChallengeCompleted = "DailyChallengeCompleted";
    public const string LessonCompleted = "LessonCompleted";
    public const string YouTubeReferenceUsed = "YouTubeReferenceUsed";
    public const string NotebookSourceUsed = "NotebookSourceUsed";
    public const string MisconceptionDetected = "MisconceptionDetected";
    public const string MistakeClassified = "MistakeClassified";
    public const string TeachingMoveApplied = "TeachingMoveApplied";
    public const string SourceCitationMissing = "SourceCitationMissing";
    public const string CentralExamPracticeAnswered = "CentralExamPracticeAnswered";
    public const string CentralExamWeaknessDetected = "CentralExamWeaknessDetected";
    public const string CentralExamDenemeAnswered = "CentralExamDenemeAnswered";
    public const string CentralExamDenemeWeaknessDetected = "CentralExamDenemeWeaknessDetected";

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
        IdeCompileError,
        IdeRuntimeError,
        IdeExecutionTimeout,
        IdeProviderUnavailable,
        IdeTestFailure,
        IdeBlankAttempt,
        IdeSentToTutor,
        RemediationStarted,
        RemediationCompleted,
        ReviewCompleted,
        DailyChallengeAssigned,
        DailyChallengeCompleted,
        LessonCompleted,
        YouTubeReferenceUsed,
        NotebookSourceUsed,
        MisconceptionDetected,
        MistakeClassified,
        TeachingMoveApplied,
        SourceCitationMissing,
        CentralExamPracticeAnswered,
        CentralExamWeaknessDetected,
        CentralExamDenemeAnswered,
        CentralExamDenemeWeaknessDetected
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var trimmed = value.Trim();
        return All.FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }
}
