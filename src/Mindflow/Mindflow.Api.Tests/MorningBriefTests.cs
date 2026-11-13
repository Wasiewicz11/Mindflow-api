using Mindflow.Api.Services;
using Mindflow.Api.Services.Ai;

namespace Mindflow.Api.Tests;

public class MorningBriefTests
{
    private static readonly DayPlan AiPlan = new(
        "Spokojny dzień z jednym spotkaniem.",
        [new DayPlanItem(new TimeOnly(9, 0), new TimeOnly(10, 0), "Spotkanie", null)],
        ["Najpierw raport"],
        ["Mało czasu po 15:00"],
        "Zacznij od trudnego.",
        true);

    [Fact]
    public void Body_lists_today_tasks_with_time_and_project_then_recommendations()
    {
        var today = new List<MorningBriefTask>
        {
            new("Raport", new DateOnly(2026, 11, 14), new TimeOnly(11, 30), "Praca"),
            new("Zakupy", new DateOnly(2026, 11, 14), null, null)
        };

        var body = MorningBrief.Body(AiPlan, today, []);

        Assert.Equal(
            "Spokojny dzień z jednym spotkaniem.\nNa dziś (2):\n• 11:30 Raport · Praca\n• Zakupy\n" +
            "Zalecenia AI:\n• Najpierw raport\nProponowany plan:\n• 09:00–10:00 Spotkanie\n" +
            "Uwaga:\n• Mało czasu po 15:00\nWskazówka: Zacznij od trudnego.",
            body);
    }

    [Fact]
    public void Body_caps_long_task_lists()
    {
        var overdue = Enumerable.Range(1, 15).Select(i => new MorningBriefTask($"Zadanie {i}", new DateOnly(2026, 11, 1), null, null)).ToList();

        var body = MorningBrief.Body(AiPlan, [], overdue);

        Assert.Contains("Zaległe (15):\n", body);
        Assert.Contains("• Zadanie 12 · termin 01.11\n… i jeszcze 3\n", body);
        Assert.DoesNotContain("Zadanie 13", body);
    }

    [Fact]
    public void Body_drops_timeline_items_to_fit_the_inbox_limit()
    {
        var timeline = Enumerable.Range(0, 40)
            .Select(i => new DayPlanItem(new TimeOnly(8, 0).AddMinutes(i * 15), new TimeOnly(8, 15).AddMinutes(i * 15), $"Punkt {i}", new string('x', 60)))
            .ToList();
        var plan = AiPlan with { Timeline = timeline };

        var body = MorningBrief.Body(plan, [], []);

        Assert.True(body.Length <= MorningBrief.MaxBodyLength);
        Assert.Contains("• 08:00–08:15 Punkt 0", body);
        Assert.Contains("… i jeszcze ", body);
        Assert.EndsWith("Wskazówka: Zacznij od trudnego.", body);
    }

    [Fact]
    public void Short_adds_counts_to_ai_summary_only()
    {
        Assert.Equal("Spokojny dzień z jednym spotkaniem.\nNa dziś: 2, zaległe: 1.", MorningBrief.Short(AiPlan, 2, 1));
        Assert.Equal("Spokojny dzień z jednym spotkaniem.\nNa dziś: 2.", MorningBrief.Short(AiPlan, 2, 0));
        Assert.Equal("Spokojny dzień z jednym spotkaniem.", MorningBrief.Short(AiPlan with { FromAi = false }, 2, 1));
        Assert.Equal("Na dziś: 0.", MorningBrief.Short(AiPlan with { Summary = " " }, 0, 0));
    }
}
