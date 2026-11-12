using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Notes;

public class NotesApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Created_note_is_listed_with_a_plain_preview()
    {
        var (client, _) = await CreateClientAsync();

        var created = await ReadAsync<NoteResponse>(client.PostAsJsonAsync("/notes",
            new SaveNoteRequest("Plan", "## Spotkanie\n- **Ustalić** budżet", ["Praca", " praca ", ""], null), MindflowApiFactory.Json));
        var list = await client.GetFromJsonAsync<List<NoteListItemResponse>>("/notes", MindflowApiFactory.Json);

        Assert.Equal(NoteSource.App, created.Source);
        Assert.Equal(NoteStatus.Ready, created.Status);
        Assert.False(created.IsNew);
        Assert.Equal(["praca"], created.Tags);
        var item = Assert.Single(list!);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal("Spotkanie Ustalić budżet", item.Preview);
    }

    [LocalDatabaseFact]
    public async Task Updated_note_moves_to_the_top_with_new_content()
    {
        var (client, _) = await CreateClientAsync();
        var first = await ReadAsync<NoteResponse>(client.PostAsJsonAsync("/notes", new SaveNoteRequest("A", "a", null, null), MindflowApiFactory.Json));
        await ReadAsync<NoteResponse>(client.PostAsJsonAsync("/notes", new SaveNoteRequest("B", "b", null, null), MindflowApiFactory.Json));

        var updated = await ReadAsync<NoteResponse>(client.PutAsJsonAsync($"/notes/{first.Id}", new SaveNoteRequest("A2", "nowa treść", ["x"], null), MindflowApiFactory.Json));
        var list = await client.GetFromJsonAsync<List<NoteListItemResponse>>("/notes", MindflowApiFactory.Json);

        Assert.Equal("A2", updated.Title);
        Assert.Equal("nowa treść", updated.Content);
        Assert.True(updated.UpdatedAt > first.UpdatedAt);
        Assert.Equal(first.CreatedAt.UtcTicks / 10, updated.CreatedAt.UtcTicks / 10);
        Assert.Equal(first.Id, list![0].Id);
    }

    [LocalDatabaseFact]
    public async Task Deleted_note_is_gone()
    {
        var (client, _) = await CreateClientAsync();
        var note = await ReadAsync<NoteResponse>(client.PostAsJsonAsync("/notes", new SaveNoteRequest("A", "a", null, null), MindflowApiFactory.Json));

        var deleted = await client.DeleteAsync($"/notes/{note.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/notes/{note.Id}")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Notes_of_another_user_are_not_found()
    {
        var (owner, _) = await CreateClientAsync();
        var (stranger, _) = await CreateClientAsync();
        var note = await ReadAsync<NoteResponse>(owner.PostAsJsonAsync("/notes", new SaveNoteRequest("Moja", "tajne", null, null), MindflowApiFactory.Json));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/notes/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/notes/{note.Id}", new SaveNoteRequest("X", "x", null, null), MindflowApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/notes/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/notes/{note.Id}/review", null)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<NoteListItemResponse>>("/notes", MindflowApiFactory.Json))!);
    }

    [LocalDatabaseFact]
    public async Task Project_must_be_accessible_and_title_not_too_long()
    {
        var (client, userId) = await CreateClientAsync();
        var stranger = await factory.CreateUserAsync();
        var foreign = await factory.CreateProjectAsync(stranger.Id, "Cudzy");
        var own = await factory.CreateProjectAsync(userId, "Mój");

        var rejected = await client.PostAsJsonAsync("/notes", new SaveNoteRequest("A", "a", null, foreign.Id), MindflowApiFactory.Json);
        var accepted = await ReadAsync<NoteResponse>(client.PostAsJsonAsync("/notes", new SaveNoteRequest("A", "a", null, own.Id), MindflowApiFactory.Json));
        var tooLong = await client.PostAsJsonAsync("/notes", new SaveNoteRequest(new string('t', 201), "a", null, null), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(own.Id, accepted.ProjectId);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Import_is_idempotent_and_keeps_creation_dates()
    {
        var (client, _) = await CreateClientAsync();
        var createdAt = new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);
        ImportNoteItem[] batch =
        [
            new("no_1", "Z Telegrama", "treść", ["Pomysły"], "telegram", null, createdAt),
            new("no_2", "Lokalna", "", null, "app", null, createdAt.AddDays(1))
        ];

        var first = await ReadAsync<ImportNotesResponse>(client.PostAsJsonAsync("/notes/import", new ImportNotesRequest(batch), MindflowApiFactory.Json));
        var second = await ReadAsync<ImportNotesResponse>(client.PostAsJsonAsync("/notes/import",
            new ImportNotesRequest([.. batch, new("no_3", "Nowa", "x", null, null, null, null)]), MindflowApiFactory.Json));
        var list = await client.GetFromJsonAsync<List<NoteListItemResponse>>("/notes", MindflowApiFactory.Json);

        Assert.Equal(new ImportNotesResponse(2, 0), first);
        Assert.Equal(new ImportNotesResponse(1, 2), second);
        Assert.Equal(3, list!.Count);
        var telegram = list.Single(n => n.Title == "Z Telegrama");
        Assert.Equal(NoteSource.Telegram, telegram.Source);
        Assert.Equal(createdAt, telegram.CreatedAt);
        Assert.Equal(["pomysły"], telegram.Tags);
        Assert.False(telegram.IsNew);
    }

    [LocalDatabaseFact]
    public async Task Review_clears_the_new_label_once()
    {
        var (client, userId) = await CreateClientAsync();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await factory.WithDbAsync(db =>
        {
            db.Notes.Add(new Note
            {
                Id = id, UserId = userId, Title = "Z dyktowania", Content = "## Co się wydarzyło",
                Source = NoteSource.Dictation, Status = NoteStatus.Ready, CreatedAt = now, UpdatedAt = now
            });
            return db.SaveChangesAsync();
        });

        var before = await client.GetFromJsonAsync<NoteResponse>($"/notes/{id}", MindflowApiFactory.Json);
        var reviewed = await ReadAsync<NoteResponse>(client.PostAsync($"/notes/{id}/review", null));
        var reviewedAt = await factory.WithDbAsync(db => Task.FromResult(db.Notes.Single(n => n.Id == id).ReviewedAt));
        await ReadAsync<NoteResponse>(client.PostAsync($"/notes/{id}/review", null));
        var reviewedAgainAt = await factory.WithDbAsync(db => Task.FromResult(db.Notes.Single(n => n.Id == id).ReviewedAt));

        Assert.True(before!.IsNew);
        Assert.False(reviewed.IsNew);
        Assert.Equal(before.UpdatedAt, reviewed.UpdatedAt);
        Assert.NotNull(reviewedAt);
        Assert.Equal(reviewedAt, reviewedAgainAt);
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
