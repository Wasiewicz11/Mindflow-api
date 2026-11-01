using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Dictation;

namespace Mindflow.Api.Tests.Ai;

public class DictationResultParserTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Home = Guid.NewGuid();

    private static readonly DictationContext Context = new(
        new DateOnly(2026, 11, 1),
        new TimeOnly(21, 30),
        "Europe/Warsaw",
        [new DictationProject(Company, "Sprawy firmowe"), new DictationProject(Home, "Dom")]);

    [Fact]
    public void Maps_tasks_dates_priorities_and_projects()
    {
        var result = DictationResultParser.Parse("""
            {"transcript":"Jutro ogarnąć dom.","tasks":[
              {"title":"ogarnąć dom","description":null,"dueDate":"2026-11-02","dueTime":"09:30","priority":"P3","project":"Dom","subtasks":[]},
              {"title":"Uporządkować dysk","dueDate":null,"priority":"P4","project":"sprawy  FIRMOWE","subtasks":["Zdjęcia"," zdjęcia ","Dokumenty"]}
            ]}
            """, Context);

        Assert.Equal("Jutro ogarnąć dom.", result.Transcript);
        Assert.Collection(result.Tasks,
            first =>
            {
                Assert.Equal("Ogarnąć dom", first.Title);
                Assert.Equal(new DateOnly(2026, 11, 2), first.DueDate);
                Assert.Equal(new TimeOnly(9, 30), first.DueTime);
                Assert.Equal(TaskPriority.P3, first.Priority);
                Assert.Equal(Home, first.ProjectId);
            },
            second =>
            {
                Assert.Null(second.DueDate);
                Assert.Equal(TaskPriority.P4, second.Priority);
                Assert.Equal(Company, second.ProjectId);
                Assert.Equal("Sprawy firmowe", second.ProjectName);
                Assert.Equal(["Zdjęcia", "Dokumenty"], second.Subtasks);
            });
    }

    [Fact]
    public void Matches_projects_ignoring_case_and_polish_diacritics_only()
    {
        Assert.Equal(Company, DictationResultParser.MatchProject("sprawy firmowe", Context.Projects)?.Id);
        Assert.Equal(Home, DictationResultParser.MatchProject("DOM", Context.Projects)?.Id);
        Assert.Null(DictationResultParser.MatchProject("Firma", Context.Projects));
        Assert.Null(DictationResultParser.MatchProject(null, Context.Projects));
    }

    [Fact]
    public void Drops_invalid_values_instead_of_trusting_them()
    {
        var result = DictationResultParser.Parse("""
            {"transcript":"x","tasks":[
              {"title":"Stara data","dueDate":"2026-10-30","dueTime":"10:00","priority":"P9"},
              {"title":"Zły format","dueDate":"02.11.2026","priority":"high"},
              {"title":"  ","priority":"P1"},
              {"title":"Godzina bez daty","dueTime":"12:00","priority":"P2"},
              "nie obiekt",
              {"title":"Za daleko","dueDate":"2040-01-01","priority":"P1"}
            ]}
            """, Context);

        Assert.Equal(["Stara data", "Zły format", "Godzina bez daty", "Za daleko"], result.Tasks.Select(t => t.Title));
        Assert.All(result.Tasks, t => Assert.Null(t.DueDate));
        Assert.All(result.Tasks, t => Assert.Null(t.DueTime));
        Assert.Equal([TaskPriority.P3, TaskPriority.P3, TaskPriority.P2, TaskPriority.P1], result.Tasks.Select(t => t.Priority));
    }

    [Fact]
    public void Removes_duplicates_and_caps_the_number_of_tasks()
    {
        var items = string.Join(",", Enumerable.Range(0, 40).Select(i => $$"""{"title":"Zadanie {{i % 35}}","priority":"P3"}"""));

        var result = DictationResultParser.Parse($$"""{"transcript":"","tasks":[{{items}}]}""", Context, maxTasks: 30);

        Assert.Equal(30, result.Tasks.Count);
        Assert.Equal(30, result.Tasks.Select(t => t.Title).Distinct().Count());
    }

    [Fact]
    public void Accepts_json_wrapped_in_a_code_fence()
    {
        var result = DictationResultParser.Parse("```json\n{\"transcript\":\"a\",\"tasks\":[{\"title\":\"Kupić mleko\",\"priority\":\"P3\"}]}\n```", Context);

        Assert.Equal("Kupić mleko", Assert.Single(result.Tasks).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("to nie jest json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"tasks\":\"nie lista\"}")]
    public void Garbage_gives_an_empty_result(string raw)
    {
        Assert.Empty(DictationResultParser.Parse(raw, Context).Tasks);
    }

    [Fact]
    public void Description_equal_to_title_is_dropped()
    {
        var result = DictationResultParser.Parse("""{"transcript":"","tasks":[{"title":"Wysłać umowę","description":"wysłać umowę","priority":"P2"}]}""", Context);

        Assert.Null(Assert.Single(result.Tasks).Description);
    }
}
