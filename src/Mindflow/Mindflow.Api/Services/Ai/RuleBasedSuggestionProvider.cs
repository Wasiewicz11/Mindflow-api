using Microsoft.Extensions.Options;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai;

public class RuleBasedSuggestionProvider(IOptions<AiOptions> options) : IAiSuggestionProvider
{
    private readonly AiOptions _options = options.Value;

    public string Name => "RuleBased";

    public bool IsConfigured => _options.EnableLocalFallback;

    public Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, CancellationToken ct = default)
    {
        var today = snapshot.Today.ToString("yyyy-MM-dd");
        var drafts = new List<SuggestionDraft>();
        var usedRefs = new HashSet<int>();

        var mostOverdue = snapshot.Tasks
            .Where(t => t.DaysUntilDue is < 0)
            .OrderBy(t => t.DaysUntilDue)
            .FirstOrDefault();
        if (mostOverdue is not null)
        {
            usedRefs.Add(mostOverdue.Ref);
            drafts.Add(new SuggestionDraft(
                "Zaległe zadanie wymaga uwagi.",
                $"Zadanie „{mostOverdue.Title}” jest po terminie o {Math.Abs(mostOverdue.DaysUntilDue!.Value)} dni. " +
                "Przestawiłem je na dziś i ustawiłem jako priorytet, żeby nie przepadło.",
                [
                    new SuggestionActionDraft(mostOverdue.Ref, SuggestionActionType.ChangeDueDate,
                        new Dictionary<string, string> { ["date"] = today }),
                    new SuggestionActionDraft(mostOverdue.Ref, SuggestionActionType.ChangePriority,
                        new Dictionary<string, string> { ["priority"] = "P1" })
                ]));
        }

        var mostPostponed = snapshot.Tasks
            .Where(t => t.TimesPostponed >= 2 && !usedRefs.Contains(t.Ref))
            .OrderByDescending(t => t.TimesPostponed)
            .FirstOrDefault();
        if (mostPostponed is not null && drafts.Count < _options.MaxSuggestionsPerRun)
        {
            usedRefs.Add(mostPostponed.Ref);
            drafts.Add(new SuggestionDraft(
                "Coś tu się odkłada.",
                $"„{mostPostponed.Title}” przesuwałeś już {mostPostponed.TimesPostponed} razy. " +
                "Może warto domknąć to dziś — ustawiłem termin na dzisiaj.",
                [
                    new SuggestionActionDraft(mostPostponed.Ref, SuggestionActionType.ChangeDueDate,
                        new Dictionary<string, string> { ["date"] = today })
                ]));
        }

        var importantNoDate = snapshot.Tasks
            .Where(t => t.Priority == "P1" && !t.HasDueDate && !usedRefs.Contains(t.Ref))
            .FirstOrDefault();
        if (importantNoDate is not null && drafts.Count < _options.MaxSuggestionsPerRun)
        {
            usedRefs.Add(importantNoDate.Ref);
            drafts.Add(new SuggestionDraft(
                "Ważne zadanie bez terminu.",
                $"„{importantNoDate.Title}” jest oznaczone jako priorytet, ale nie ma terminu. " +
                "Proponuję zacząć dziś.",
                [
                    new SuggestionActionDraft(importantNoDate.Ref, SuggestionActionType.ChangeDueDate,
                        new Dictionary<string, string> { ["date"] = today })
                ]));
        }

        return Task.FromResult<IReadOnlyList<SuggestionDraft>>(drafts);
    }
}
