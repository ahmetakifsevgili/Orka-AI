namespace Orka.Core.DTOs;

public sealed class DashboardTodayDto
{
    public string DailyFocusTitle { get; set; } = "Bugün";
    public string DailyFocusReason { get; set; } = "Başlamak için bir konu seç.";
    public DashboardNextActionDto NextAction { get; set; } = new();
    public IReadOnlyList<DashboardWeakConceptDto> WeakConcepts { get; set; } = Array.Empty<DashboardWeakConceptDto>();
    public DashboardSourceHealthDto SourceHealth { get; set; } = new();
    public int DueReviewCount { get; set; }
    public DashboardActivePlanDto? ActivePlan { get; set; }
    public DashboardCoordinationScopeDto? CoordinationScope { get; set; }
    public DashboardCoordinationHealthDto? CoordinationHealth { get; set; }
    public DashboardEntryPointDto RecommendedEntryPoint { get; set; } = new();
    public bool HasRealLearningData { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardNextActionDto
{
    public string Label { get; set; } = "Öğrenmeye başla";
    public string Reason { get; set; } = "Henüz yeterli öğrenme izi yok.";
    public string View { get; set; } = "chat";
    public Guid? TopicId { get; set; }
    public string UserSafeStatus { get; set; } = "Hazır";
}

public sealed class DashboardWeakConceptDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public Guid? TopicId { get; set; }
    public string UserSafeStatus { get; set; } = "Kanıt düşük";
}

public sealed class DashboardSourceHealthDto
{
    public string Status { get; set; } = "unknown";
    public string UserSafeLabel { get; set; } = "Kaynak durumu bilinmiyor";
    public string UserSafeDetail { get; set; } = "Kaynak eklenince cevaplar daha güvenli hale gelir.";
    public decimal CitationCoverage { get; set; }
    public int UnsupportedCitationCount { get; set; }
}

public sealed class DashboardActivePlanDto
{
    public Guid TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
}

public sealed class DashboardCoordinationScopeDto
{
    public Guid? RootTopicId { get; set; }
    public Guid? CurrentTopicId { get; set; }
    public Guid? ActiveLessonTopicId { get; set; }
    public int TreeTopicCount { get; set; }
    public int SourceCount { get; set; }
    public int QuizAttemptCount { get; set; }
    public int LearningSignalCount { get; set; }
}

public sealed class DashboardCoordinationHealthDto
{
    public string OverallStatus { get; set; } = "unknown";
    public string UserSafeSummary { get; set; } = "Coordination durumu izleniyor.";
    public int WindowDays { get; set; } = 7;
    public Guid? RootTopicId { get; set; }
    public Guid? CurrentTopicId { get; set; }
    public Guid? ActiveLessonTopicId { get; set; }
    public IReadOnlyList<DashboardCoordinationHealthMetricDto> Metrics { get; set; } = Array.Empty<DashboardCoordinationHealthMetricDto>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardCoordinationHealthMetricDto
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public int Count { get; set; }
    public int Total { get; set; }
    public decimal Ratio { get; set; }
    public string UserSafeLabel { get; set; } = string.Empty;
    public string UserSafeDetail { get; set; } = string.Empty;
}

public sealed class DashboardEntryPointDto
{
    public string View { get; set; } = "chat";
    public string Label { get; set; } = "Öğren";
    public string Reason { get; set; } = "Tutor ile devam et.";
}
