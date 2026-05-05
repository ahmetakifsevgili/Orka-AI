def validate_keys(data, expected_keys):
    """
    Strictly validates that all expected keys exist in the response data.
    """
    actual_keys = set(data.keys())
    missing_keys = set(expected_keys) - actual_keys
    if missing_keys:
        raise AssertionError(f"Response missing mandatory keys: {missing_keys}. Actual keys: {actual_keys}")

def validate_topic_schema(topic):
    expected = ["id", "title", "emoji", "category", "createdAt"]
    validate_keys(topic, expected)

def validate_user_me_schema(user):
    expected = ["id", "email", "settings"]
    validate_keys(user, expected)

    settings_expected = ["theme", "language", "fontSize", "quizReminders", "weeklyReport", "newContentAlerts", "soundsEnabled"]
    validate_keys(user["settings"], settings_expected)

def validate_dashboard_stats_schema(stats):
    expected = [
        "totalXP", "currentStreak", "completedTopics", "activeLearning",
        "totalTopics", "completedSections", "totalSections",
        "progressPercentage", "activity", "learningSignalBook"
    ]
    validate_keys(stats, expected)
