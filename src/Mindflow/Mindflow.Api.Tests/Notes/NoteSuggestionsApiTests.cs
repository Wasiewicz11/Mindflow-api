using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Notes;

public class NoteSuggestionsApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Accepting_creates_a_task_with_the_suggested_fields()
    {
        var (client, userId) = await CreateClientAsync();
        var project = await factory.CreateProjectAsync(userId, "Firma");
        var due = new DateOnly(2030, 5, 20);
        var note = await CreateNoteAsync(userId, Suggestion("Wysłać ofertę", due: due, time: new TimeOnly(10, 30), priority: TaskPriority.P2, projectId: project.Id, subtasks: ["Przygotować wycenę", "Sprawdzić stawki"]));

        var response = await client.PostAsync($"/notes/{note.Id}/suggestions/{note.Suggestions[0]}/accept", null);
        var accepted = await response.Content.ReadFromJsonAsync<NoteSuggestionResponse>(MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(NoteSuggestionStatus.Accepted, accepted!.Status);
        Assert.Equal("Firma", accepted.ProjectName);
        var task = await client.GetFromJsonAsync<TaskDetailResponse>($"/tasks/{accepted.TaskId}", MindflowApiFactory.Json);
        Assert.Equal("Wysłać ofertę", task!.Content);
        Assert.Equal(due, task.DueDate);
        Assert.Equal(new TimeOnly(10, 30), task.DueTime);
        Assert.Equal(TaskPriority.P2, task.Priority);
        Assert.Equal(project.Id, task.ProjectId);
        Assert.Equal(["Przygotować wycenę", "Sprawdzić stawki"], task.Subtasks.Select(s => s.Content).Order());
    }

    [LocalDatabaseFact]
    public async Task Accepting_with_overrides_changes_the_task_and_the_suggestion()
    {
        var (client, userId) = await CreateClientAsync();
        var note = await CreateNoteAsync(userId, Suggestion("Zadzwonić", due: new DateOnly(2030, 5, 20), time: new TimeOnly(9, 0)));
        var newDue = new DateOnly(2030, 6, 1);

        var response = await client.PostAsJsonAsync($"/notes/{note.Id}/suggestions/{note.Suggestions[0]}/accept",
            new AcceptNoteSuggestionRequest(Title: "Zadzwonić do Ani", DueDate: newDue), MindflowApiFactory.Json);
        var accepted = await response.Content.ReadFromJsonAsync<NoteSuggestionResponse>(MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Zadzwonić do Ani", accepted!.Title);
        Assert.Equal(newDue, accepted.DueDate);
        var task = await client.GetFromJsonAsync<TaskDetailResponse>($"/tasks/{accepted.TaskId}", MindflowApiFactory.Json);
        Assert.Equal("Zadzwonić do Ani", task!.Content);
        Assert.Equal(newDue, task.DueDate);
    }

    [LocalDatabaseFact]
    public async Task Accepting_can_clear_the_due_date_and_the_project()
    {
        var (client, userId) = await CreateClientAsync();
        var project = await factory.CreateProjectAsync(userId, "Firma");
        var note = await CreateNoteAsync(userId, Suggestion("Zadanie", due: new DateOnly(2030, 5, 20), projectId: project.Id));

        var response = await client.PostAsJsonAsync($"/notes/{note.Id}/suggestions/{note.Suggestions[0]}/accept",
            new AcceptNoteSuggestionRequest(ClearDueDate: true, ClearProject: true), MindflowApiFactory.Json);
        var accepted = await response.Content.ReadFromJsonAsync<NoteSuggestionResponse>(MindflowApiFactory.Json);

        Assert.Null(accepted!.DueDate);
        Assert.Null(accepted.ProjectId);
        var task = await client.GetFromJsonAsync<TaskDetailResponse>($"/tasks/{accepted.TaskId}", MindflowApiFactory.Json);
        Assert.Null(task!.DueDate);
        Assert.Null(task.ProjectId);
    }

    [LocalDatabaseFact]
    public async Task Accepting_twice_is_rejected_and_creates_one_task()
    {
        var (client, userId) = await CreateClientAsync();
        var note = await CreateNoteAsync(userId, Suggestion("Raz"));
        var url = $"/notes/{note.Id}/suggestions/{note.Suggestions[0]}/accept";

        var first = await client.PostAsync(url, null);
        var second = await client.PostAsync(url, null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(1, await factory.WithDbAsync(db => db.Tasks.CountAsync(t => t.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task Accepting_with_an_inaccessible_project_fails_and_keeps_the_suggestion_pending()
    {
        var (client, userId) = await CreateClientAsync();
        var (_, strangerId) = await CreateClientAsync();
        var foreign = await factory.CreateProjectAsync(strangerId, "Cudzy");
        var note = await CreateNoteAsync(userId, Suggestion("Zadanie"));

        var response = await client.PostAsJsonAsync($"/notes/{note.Id}/suggestions/{note.Suggestions[0]}/accept",
            new AcceptNoteSuggestionRequest(ProjectId: foreign.Id), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await factory.WithDbAsync(db => db.NoteTaskSuggestions.AsNoTracking().SingleAsync(s => s.Id == note.Suggestions[0]));
        Assert.Equal(NoteSuggestionStatus.Pending, stored.Status);
        Assert.Null(stored.TaskId);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Tasks.CountAsync(t => t.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task Rejected_suggestion_can_be_restored_but_an_accepted_one_cannot()
    {
        var (client, userId) = await CreateClientAsync();
        var note = await CreateNoteAsync(userId, Suggestion("A"), Suggestion("B"));
        var (a, b) = (note.Suggestions[0], note.Suggestions[1]);

        var rejected = await ReadAsync<NoteSuggestionResponse>(client.PostAsync($"/notes/{note.Id}/suggestions/{a}/reject", null));
        Assert.Equal(NoteSuggestionStatus.Rejected, rejected.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/notes/{note.Id}/suggestions/{a}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/notes/{note.Id}/suggestions/{a}/accept", null)).StatusCode);

        var restored = await ReadAsync<NoteSuggestionResponse>(client.PostAsync($"/notes/{note.Id}/suggestions/{a}/restore", null));
        Assert.Equal(NoteSuggestionStatus.Pending, restored.Status);

        await ReadAsync<NoteSuggestionResponse>(client.PostAsync($"/notes/{note.Id}/suggestions/{b}/accept", null));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/notes/{note.Id}/suggestions/{b}/restore", null)).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Accepted_suggestion_without_a_task_can_be_restored()
    {
        var (client, userId) = await CreateClientAsync();
        var note = await CreateNoteAsync(userId, Suggestion("A"));
        var id = note.Suggestions[0];
        await factory.WithDbAsync(db => db.NoteTaskSuggestions.Where(s => s.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, NoteSuggestionStatus.Accepted)));

        var restored = await ReadAsync<NoteSuggestionResponse>(client.PostAsync($"/notes/{note.Id}/suggestions/{id}/restore", null));

        Assert.Equal(NoteSuggestionStatus.Pending, restored.Status);
    }

    [LocalDatabaseFact]
    public async Task Accepting_rejects_oversized_overrides_and_keeps_the_suggestion_pending()
    {
        var (client, userId) = await CreateClientAsync();
        var note = await CreateNoteAsync(userId, Suggestion("A"));
        var url = $"/notes/{note.Id}/suggestions/{note.Suggestions[0]}/accept";

        var tooMany = await client.PostAsJsonAsync(url, new AcceptNoteSuggestionRequest(Subtasks: Enumerable.Range(0, 21).Select(i => $"p{i}").ToList()), MindflowApiFactory.Json);
        var tooLong = await client.PostAsJsonAsync(url, new AcceptNoteSuggestionRequest(Subtasks: [new string('x', 1001)]), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal("Za dużo podzadań.", await ErrorAsync(tooMany));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("Za długie podzadanie.", await ErrorAsync(tooLong));
        var stored = await factory.WithDbAsync(db => db.NoteTaskSuggestions.AsNoTracking().SingleAsync(s => s.Id == note.Suggestions[0]));
        Assert.Equal(NoteSuggestionStatus.Pending, stored.Status);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Tasks.CountAsync(t => t.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task Accepting_with_no_due_date_left_drops_the_time()
    {
        var (client, userId) = await CreateClientAsync();
        var note = await CreateNoteAsync(userId, Suggestion("A", time: new TimeOnly(9, 0)));

        var accepted = await ReadAsync<NoteSuggestionResponse>(client.PostAsJsonAsync(
            $"/notes/{note.Id}/suggestions/{note.Suggestions[0]}/accept", new AcceptNoteSuggestionRequest(Title: "B"), MindflowApiFactory.Json));

        Assert.Null(accepted.DueDate);
        Assert.Null(accepted.DueTime);
    }

    [LocalDatabaseFact]
    public async Task Accept_all_skips_rejected_suggestions()
    {
        var (client, userId) = await CreateClientAsync();
        var note = await CreateNoteAsync(userId, Suggestion("A"), Suggestion("B"), Suggestion("C"));
        await ReadAsync<NoteSuggestionResponse>(client.PostAsync($"/notes/{note.Id}/suggestions/{note.Suggestions[1]}/reject", null));

        var result = await ReadAsync<NoteResponse>(client.PostAsync($"/notes/{note.Id}/suggestions/accept-all", null));

        Assert.Equal([NoteSuggestionStatus.Accepted, NoteSuggestionStatus.Rejected, NoteSuggestionStatus.Accepted], result.Suggestions.Select(s => s.Status));
        Assert.All(result.Suggestions.Where(s => s.Status == NoteSuggestionStatus.Accepted), s => Assert.NotNull(s.TaskId));
        var titles = await factory.WithDbAsync(db => db.Tasks.Where(t => t.UserId == userId).OrderBy(t => t.Content).Select(t => t.Content).ToListAsync());
        Assert.Equal(["A", "C"], titles);
    }

    [LocalDatabaseFact]
    public async Task Note_and_list_expose_suggestions_and_the_pending_count()
    {
        var (client, userId) = await CreateClientAsync();
        var project = await factory.CreateProjectAsync(userId, "Firma");
        var note = await CreateNoteAsync(userId, Suggestion("A", projectId: project.Id), Suggestion("B"));
        await ReadAsync<NoteSuggestionResponse>(client.PostAsync($"/notes/{note.Id}/suggestions/{note.Suggestions[1]}/reject", null));

        var detail = await client.GetFromJsonAsync<NoteResponse>($"/notes/{note.Id}", MindflowApiFactory.Json);
        var list = await client.GetFromJsonAsync<List<NoteListItemResponse>>("/notes", MindflowApiFactory.Json);

        Assert.Equal(["A", "B"], detail!.Suggestions.Select(s => s.Title));
        Assert.Equal("Firma", detail.Suggestions[0].ProjectName);
        Assert.Equal(1, list!.Single(n => n.Id == note.Id).PendingSuggestions);
    }

    [LocalDatabaseFact]
    public async Task Another_users_note_is_not_found_on_every_endpoint()
    {
        var (_, ownerId) = await CreateClientAsync();
        var (stranger, _) = await CreateClientAsync();
        var note = await CreateNoteAsync(ownerId, Suggestion("A"));
        var baseUrl = $"/notes/{note.Id}/suggestions";

        foreach (var url in new[] { $"{baseUrl}/{note.Suggestions[0]}/accept", $"{baseUrl}/{note.Suggestions[0]}/reject", $"{baseUrl}/{note.Suggestions[0]}/restore", $"{baseUrl}/accept-all" })
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync(url, null)).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Suggestion_of_another_note_is_not_found()
    {
        var (client, userId) = await CreateClientAsync();
        var first = await CreateNoteAsync(userId, Suggestion("A"));
        var second = await CreateNoteAsync(userId, Suggestion("B"));

        var response = await client.PostAsync($"/notes/{first.Id}/suggestions/{second.Suggestions[0]}/accept", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record SeededNote(Guid Id, List<Guid> Suggestions);

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("error").GetString();
    }

    private static NoteTaskSuggestion Suggestion(string title, DateOnly? due = null, TimeOnly? time = null, TaskPriority priority = TaskPriority.P3, Guid? projectId = null, string[]? subtasks = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            DueDate = due,
            DueTime = time,
            Priority = priority,
            ProjectId = projectId,
            Subtasks = subtasks?.ToList() ?? [],
            Status = NoteSuggestionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private async Task<SeededNote> CreateNoteAsync(Guid userId, params NoteTaskSuggestion[] suggestions)
    {
        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Notatka",
            Content = "Treść",
            Source = NoteSource.Dictation,
            Status = NoteStatus.Ready,
            CreatedAt = now,
            UpdatedAt = now
        };
        await factory.WithDbAsync(async db =>
        {
            db.Notes.Add(note);
            for (var i = 0; i < suggestions.Length; i++)
            {
                suggestions[i].NoteId = note.Id;
                suggestions[i].Order = i;
                db.NoteTaskSuggestions.Add(suggestions[i]);
            }
            await db.SaveChangesAsync();
            return 0;
        });
        return new SeededNote(note.Id, suggestions.Select(s => s.Id).ToList());
    }

    private static async Task<T> ReadAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(MindflowApiFactory.Json))!;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync()
    {
        var user = await factory.CreateUserAsync();
        return (factory.CreateUserClient(user.Id), user.Id);
    }
}
