namespace Mindflow.Api.Services.Ai;

/// <summary>Rule-based day plan built from the same data the AI gets.</summary>
public static class DayPlanFallback
{
    private const int DefaultTaskMinutes = 45;
    private const int BreakMinutes = 15;
    private const int OverloadMinutes = 8 * 60;
    private static readonly TimeOnly DayEnd = new(22, 0);

    public static DayPlan Build(DayPlanContext context)
    {
        var blockedRefs = context.Blocks.Where(b => b.TaskRef is not null).Select(b => b.TaskRef!.Value).ToHashSet();
        var dueToday = context.Tasks.Where(t => t.DaysUntilDue == 0).ToList();
        var overdue = context.Tasks.Where(t => t.DaysUntilDue < 0).ToList();

        var fixedItems = context.Blocks
            .Select(b => new DayPlanItem(b.Start, b.End, Short(b.Title), "Stały punkt z kalendarza."))
            .ToList();
        fixedItems.AddRange(dueToday
            .Where(t => t.DueTime is not null && !blockedRefs.Contains(t.Ref))
            .Select(t => new DayPlanItem(t.DueTime!.Value, EndOf(t.DueTime.Value, Minutes(t)), Short(t.Title), $"Termin dziś o tej godzinie · {t.Priority}")));

        var occupied = fixedItems.Select(i => (i.Start, i.End)).ToList();
        var flexible = new List<DayPlanItem>();
        var unplaced = 0;
        var queue = dueToday.Where(t => t.DueTime is null).Concat(overdue)
            .Where(t => !blockedRefs.Contains(t.Ref))
            .OrderBy(t => t.Priority, StringComparer.Ordinal)
            .ThenBy(t => t.DaysUntilDue)
            .ToList();
        foreach (var task in queue)
        {
            if (FindSlot(occupied, context.DayStart, Minutes(task)) is not { } start)
            {
                unplaced++;
                continue;
            }

            var end = start.AddMinutes(Minutes(task));
            occupied.Add((start, end));
            flexible.Add(new DayPlanItem(start, end, Short(task.Title), TaskNote(task)));

            if (flexible.Count == 2 && FindSlot(occupied, end, BreakMinutes) is { } pause)
            {
                occupied.Add((pause, pause.AddMinutes(BreakMinutes)));
                flexible.Add(new DayPlanItem(pause, pause.AddMinutes(BreakMinutes), "Przerwa", "Krótki reset przed kolejnym zadaniem."));
            }
        }

        var timeline = fixedItems.Concat(flexible)
            .OrderBy(i => i.Start)
            .Take(DayPlanPromptBuilder.MaxTimelineItems)
            .ToList();

        var focus = dueToday.Concat(overdue)
            .OrderBy(t => t.Priority, StringComparer.Ordinal)
            .ThenBy(t => t.DaysUntilDue)
            .Select(t => Short(t.Title))
            .Distinct()
            .Take(DayPlanPromptBuilder.MaxFocusItems)
            .ToList();
        if (focus.Count == 0)
            focus = context.Tasks
                .Where(t => t.Priority is "P1" or "P2")
                .OrderBy(t => t.Priority, StringComparer.Ordinal)
                .Select(t => Short(t.Title))
                .Take(DayPlanPromptBuilder.MaxFocusItems)
                .ToList();

        return new DayPlan(
            Summary(context, dueToday.Count, overdue.Count, focus),
            timeline,
            focus,
            Risks(context, overdue.Count, timeline, unplaced),
            overdue.Count > 0
                ? "Zacznij od jednego zaległego zadania, zanim otworzysz pocztę."
                : "Najważniejsze zadanie zrób przed południem, w jednym bloku bez powiadomień.",
            FromAi: false);
    }

    private static string Summary(DayPlanContext context, int dueToday, int overdue, IReadOnlyList<string> focus)
    {
        if (dueToday == 0 && overdue == 0 && context.Blocks.Count == 0)
            return "Dziś nie masz zadań z terminem ani bloków w kalendarzu. Dobry dzień na zadania bez terminu albo planowanie.";

        var parts = new List<string> { $"Na dziś: {Plural(dueToday, "zadanie", "zadania", "zadań")}" };
        if (overdue > 0) parts.Add($"zaległe: {overdue}");
        if (context.Blocks.Count > 0) parts.Add($"bloki w kalendarzu: {context.Blocks.Count}");
        var summary = string.Join(", ", parts) + ".";
        return focus.Count > 0 ? $"{summary} Główny cel: {focus[0]}." : summary;
    }

    private static List<string> Risks(DayPlanContext context, int overdue, IReadOnlyList<DayPlanItem> timeline, int unplaced)
    {
        var risks = new List<string>();
        if (overdue > 0)
            risks.Add($"Po terminie: {Plural(overdue, "zadanie", "zadania", "zadań")}.");

        var blocks = context.Blocks.OrderBy(b => b.Start).ToList();
        for (var i = 1; i < blocks.Count; i++)
            if (blocks[i].Start < blocks[i - 1].End)
            {
                risks.Add($"Nakładają się bloki: {Short(blocks[i - 1].Title)} i {Short(blocks[i].Title)}.");
                break;
            }

        var plannedMinutes = timeline.Sum(i => (i.End - i.Start).TotalMinutes);
        if (plannedMinutes > OverloadMinutes)
            risks.Add($"Dużo pracy: ok. {Math.Round(plannedMinutes / 60, 1)} h w planie.");
        if (unplaced > 0)
            risks.Add($"Nie zmieści się dziś: {Plural(unplaced, "zadanie", "zadania", "zadań")}.");
        return risks.Take(DayPlanPromptBuilder.MaxRiskItems).ToList();
    }

    private static TimeOnly? FindSlot(List<(TimeOnly Start, TimeOnly End)> occupied, TimeOnly from, int minutes)
    {
        var start = from;
        foreach (var (busyStart, busyEnd) in occupied.OrderBy(o => o.Start))
        {
            if (busyEnd <= start) continue;
            if (busyStart >= start.AddMinutes(minutes)) break;
            start = busyEnd;
        }

        var end = start.AddMinutes(minutes);
        return end > start && end <= DayEnd ? start : null;
    }

    private static int Minutes(DayPlanTask task) =>
        task.EstimatedHours is > 0 ? (int)Math.Clamp(Math.Round(task.EstimatedHours.Value * 60), 15, 240) : DefaultTaskMinutes;

    private static TimeOnly EndOf(TimeOnly start, int minutes)
    {
        var end = start.AddMinutes(minutes);
        return end > start ? end : new TimeOnly(23, 59);
    }

    private static string TaskNote(DayPlanTask task) => task.DaysUntilDue switch
    {
        < 0 => $"Po terminie {Plural(-task.DaysUntilDue.Value, "dzień", "dni", "dni")} · {task.Priority}",
        _ => $"Termin dziś · {task.Priority}"
    };

    private static string Short(string title) => DayPlanPromptBuilder.Truncate(title, DayPlanPromptBuilder.MaxTitleLength);

    private static string Plural(int count, string one, string few, string many) =>
        count == 1 ? $"1 {one}"
        : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? $"{count} {few}"
        : $"{count} {many}";
}
