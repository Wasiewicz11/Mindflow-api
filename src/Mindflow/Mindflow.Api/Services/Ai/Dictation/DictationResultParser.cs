using System.Globalization;
using System.Text;
using System.Text.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai.Dictation;

public record DictationResult(string Transcript, IReadOnlyList<DictatedTaskResponse> Tasks);

/// <summary>Turns the model's JSON into tasks the API accepts; anything malformed is dropped, never trusted.</summary>
public static class DictationResultParser
{
    private const int MaxTitle = 1000;
    private const int MaxDescription = 10000;
    private const int MaxSubtasks = 20;
    private const int MaxTranscript = 20000;

    public static DictationResult Parse(string? raw, DictationContext context, int maxTasks = 30)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new DictationResult("", []);

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(StripFences(raw));
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new DictationResult("", []);
        }

        if (root.ValueKind != JsonValueKind.Object) return new DictationResult("", []);

        var transcript = Clip(String(root, "transcript"), MaxTranscript) ?? "";
        var tasks = new List<DictatedTaskResponse>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("tasks", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (tasks.Count >= maxTasks) break;
                var task = ParseTask(item, context);
                if (task is not null && seen.Add($"{task.Title}|{task.DueDate}")) tasks.Add(task);
            }
        }

        return new DictationResult(transcript, tasks);
    }

    private static DictatedTaskResponse? ParseTask(JsonElement item, DictationContext context)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        var title = Clip(Capitalize(Collapse(String(item, "title"))), MaxTitle);
        if (title is null) return null;

        var description = Clip(String(item, "description")?.Trim(), MaxDescription);
        if (description is not null && (description.Length == 0 || string.Equals(description, title, StringComparison.OrdinalIgnoreCase)))
            description = null;

        var dueDate = ParseDate(String(item, "dueDate"), context.Today);
        var dueTime = dueDate is null ? null : ParseTime(String(item, "dueTime"));
        var priority = Enum.TryParse<TaskPriority>(String(item, "priority")?.Trim(), ignoreCase: true, out var p) && Enum.IsDefined(p)
            ? p
            : TaskPriority.P3;
        var project = MatchProject(String(item, "project"), context.Projects);

        var subtasks = new List<string>();
        if (item.TryGetProperty("subtasks", out var subs) && subs.ValueKind == JsonValueKind.Array)
        {
            foreach (var sub in subs.EnumerateArray())
            {
                var text = sub.ValueKind == JsonValueKind.String ? Clip(Capitalize(Collapse(sub.GetString())), MaxTitle) : null;
                if (text is not null && !subtasks.Contains(text, StringComparer.OrdinalIgnoreCase) && subtasks.Count < MaxSubtasks)
                    subtasks.Add(text);
            }
        }

        return new DictatedTaskResponse(title, description, dueDate, dueTime, priority, project?.Id, project?.Name, subtasks);
    }

    public static DictationProject? MatchProject(string? name, IReadOnlyList<DictationProject> projects)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var wanted = name.Trim();
        return projects.FirstOrDefault(p => string.Equals(p.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            ?? projects.FirstOrDefault(p => Fold(p.Name) == Fold(wanted));
    }

    private static DateOnly? ParseDate(string? value, DateOnly today)
    {
        if (!DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        return date < today || date > today.AddYears(5) ? null : date;
    }

    private static TimeOnly? ParseTime(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return TimeOnly.TryParseExact(text, ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '-', '•', '*');

    private static string? Capitalize(string? value) =>
        string.IsNullOrEmpty(value) ? null : char.ToUpperInvariant(value[0]) + value[1..];

    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    private static string Fold(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(c))
                builder.Append(c);
        return builder.ToString();
    }

    internal static string StripFences(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var start = text.IndexOf('\n');
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        return start > 0 && end > start ? text[(start + 1)..end] : text;
    }
}
