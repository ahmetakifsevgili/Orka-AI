namespace Orka.Infrastructure.Services;

public static class ReviewIdentitySelector
{
    public static string Select(
        string? conceptTag,
        string? skillTag,
        string? learningObjective,
        string? topicPath)
    {
        var selected = FirstNonBlank(conceptTag, skillTag, learningObjective, topicPath, "unknown skill");
        selected = string.Join(' ', selected.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return selected.Length <= 120 ? selected : selected[..120];
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "unknown skill";
}
