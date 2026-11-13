using System.Globalization;
using Mindflow.Api.Services.Ai;

namespace Mindflow.Api.Services;

public record MorningBriefTask(string Title, DateOnly? DueDate, TimeOnly? DueTime, string? Project);

/// <summary>Morning brief text built from the day plan and the user's tasks due today or overdue.</summary>
public static class MorningBrief
{
    public const int MaxBodyLength = 2000;
    private const int MaxSummaryLength = 200;
    private const int MaxTitleLength = 80;
    private const int MaxTasksPerList = 12;

    public static string Short(DayPlan plan, int todayCount, int overdueCount)
    {
        var counts = overdueCount == 0
            ? $"Na dziś: {todayCount}."
            : $"Na dziś: {todayCount}, zaległe: {overdueCount}.";
        var summary = Truncate(plan.Summary.Trim(), MaxSummaryLength);
        if (summary.Length == 0) return counts;
        return plan.FromAi ? $"{summary}\n{counts}" : summary;
    }

    public static string Body(DayPlan plan, IReadOnlyList<MorningBriefTask> today, IReadOnlyList<MorningBriefTask> overdue)
    {
        for (var shown = plan.Timeline.Count; ; shown--)
        {
            var text = Compose(plan, today, overdue, shown);
            if (text.Length <= MaxBodyLength) return text;
            if (shown == 0) return Truncate(text, MaxBodyLength);
        }
    }

    private static string Compose(DayPlan plan, IReadOnlyList<MorningBriefTask> today, IReadOnlyList<MorningBriefTask> overdue, int shownItems)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.Summary)) lines.Add(plan.Summary.Trim());

        if (today.Count == 0)
            lines.Add("Na dziś: brak zadań z terminem.");
        else
            AddTasks(lines, $"Na dziś ({today.Count}):", today, TodayLine);

        if (overdue.Count > 0)
            AddTasks(lines, $"Zaległe ({overdue.Count}):", overdue, OverdueLine);

        AddList(lines, plan.FromAi ? "Zalecenia AI:" : "Zalecenia:", plan.Focus);

        if (shownItems > 0)
        {
            lines.Add("Proponowany plan:");
            foreach (var item in plan.Timeline.Take(shownItems))
            {
                var note = string.IsNullOrWhiteSpace(item.Note) ? "" : $" ({item.Note.Trim()})";
                lines.Add($"• {Time(item.Start)}–{Time(item.End)} {item.Title}{note}");
            }

            if (shownItems < plan.Timeline.Count)
                lines.Add($"… i jeszcze {plan.Timeline.Count - shownItems}");
        }

        AddList(lines, "Uwaga:", plan.Risks);
        if (!string.IsNullOrWhiteSpace(plan.Tip)) lines.Add($"Wskazówka: {plan.Tip.Trim()}");

        return string.Join('\n', lines);
    }

    private static void AddTasks(
        List<string> lines,
        string header,
        IReadOnlyList<MorningBriefTask> tasks,
        Func<MorningBriefTask, string> format)
    {
        lines.Add(header);
        lines.AddRange(tasks.Take(MaxTasksPerList).Select(task => $"• {format(task)}"));
        if (tasks.Count > MaxTasksPerList) lines.Add($"… i jeszcze {tasks.Count - MaxTasksPerList}");
    }

    private static void AddList(List<string> lines, string header, IReadOnlyList<string> items)
    {
        var shown = items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        if (shown.Count == 0) return;
        lines.Add(header);
        lines.AddRange(shown.Select(item => $"• {item.Trim()}"));
    }

    private static string TodayLine(MorningBriefTask task)
    {
        var time = task.DueTime is { } due ? $"{Time(due)} " : "";
        return $"{time}{Truncate(task.Title, MaxTitleLength)}{ProjectSuffix(task)}";
    }

    private static string OverdueLine(MorningBriefTask task)
    {
        var due = task.DueDate is { } date ? $" · termin {date.ToString("dd.MM", CultureInfo.InvariantCulture)}" : "";
        return $"{Truncate(task.Title, MaxTitleLength)}{ProjectSuffix(task)}{due}";
    }

    private static string ProjectSuffix(MorningBriefTask task) =>
        string.IsNullOrWhiteSpace(task.Project) ? "" : $" · {task.Project}";

    private static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..Math.Max(0, maxLength - 1)]}…";
}
