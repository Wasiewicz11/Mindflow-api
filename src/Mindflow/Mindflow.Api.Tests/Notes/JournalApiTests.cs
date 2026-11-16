using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Notes;

public class JournalApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private static readonly SaveNoteRequest Empty = new("", "", null, null);

    [LocalDatabaseFact]
    public async Task Day_without_a_note_is_empty_and_saving_nothing_creates_nothing()
    {
        var (client, userId) = await CreateClientAsync();

        var get = await client.GetAsync("/notes/journal/2030-03-01");
        var put = await client.PutAsJsonAsync("/notes/journal/2030-03-01", Empty, MindflowApiFactory.Json);
        var blank = await client.PutAsJsonAsync("/notes/journal/2030-03-02", new SaveNoteRequest(" ", "  \n ", null, null), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.NoContent, get.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, blank.StatusCode);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Notes.CountAsync(n => n.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task First_content_creates_the_day_note_and_next_saves_update_it()
    {
        var (client, userId) = await CreateClientAsync();

        var created = await SaveAsync(client, "2030-03-01", "Pierwszy wpis");
        var updated = await SaveAsync(client, "2030-03-01", "Pierwszy wpis\n\nI drugi akapit");
        var read = await client.GetFromJsonAsync<NoteResponse>("/notes/journal/2030-03-01", MindflowApiFactory.Json);

        Assert.Equal(new DateOnly(2030, 3, 1), created.JournalDate);
        Assert.False(created.IsNew);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Pierwszy wpis\n\nI drugi akapit", read!.Content);
        Assert.Equal(1, await factory.WithDbAsync(db => db.Notes.CountAsync(n => n.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task Clearing_the_content_removes_the_day_note()
    {
        var (client, userId) = await CreateClientAsync();
        await SaveAsync(client, "2030-03-01", "Do usunięcia");

        var cleared = await client.PutAsJsonAsync("/notes/journal/2030-03-01", Empty, MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/notes/journal/2030-03-01")).StatusCode);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Notes.CountAsync(n => n.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task Range_lists_only_days_with_notes_and_regular_list_skips_them()
    {
        var (client, _) = await CreateClientAsync();
        await SaveAsync(client, "2030-03-01", "**Marzec** pierwszy");
        await SaveAsync(client, "2030-03-15", "Połowa");
        await SaveAsync(client, "2030-04-01", "Kwiecień");
        var regular = await ReadAsync<NoteResponse>(client.PostAsJsonAsync("/notes", new SaveNoteRequest("Zwykła", "treść", null, null), MindflowApiFactory.Json));

        var days = await client.GetFromJsonAsync<List<JournalDayResponse>>("/notes/journal?from=2030-03-01&to=2030-03-31", MindflowApiFactory.Json);
        var list = await client.GetFromJsonAsync<List<NoteListItemResponse>>("/notes", MindflowApiFactory.Json);

        Assert.Equal([new DateOnly(2030, 3, 1), new DateOnly(2030, 3, 15)], days!.Select(d => d.Date));
        Assert.Equal("Marzec pierwszy", days[0].Preview);
        var item = Assert.Single(list!);
        Assert.Equal(regular.Id, item.Id);
        Assert.Null(item.JournalDate);
    }

    [LocalDatabaseFact]
    public async Task Range_must_be_ordered_and_at_most_a_year_long()
    {
        var (client, _) = await CreateClientAsync();

        var reversed = await client.GetAsync("/notes/journal?from=2030-03-31&to=2030-03-01");
        var tooLong = await client.GetAsync("/notes/journal?from=2030-01-01&to=2031-01-02");

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Journal_is_private_to_its_owner()
    {
        var (owner, _) = await CreateClientAsync();
        var (stranger, strangerId) = await CreateClientAsync();
        var note = await SaveAsync(owner, "2030-03-01", "Moje");

        var strangerDay = await stranger.GetAsync("/notes/journal/2030-03-01");
        var strangerList = await stranger.GetFromJsonAsync<List<JournalDayResponse>>("/notes/journal?from=2030-03-01&to=2030-03-31", MindflowApiFactory.Json);
        var own = await SaveAsync(stranger, "2030-03-01", "Też moje");

        Assert.Equal(HttpStatusCode.NoContent, strangerDay.StatusCode);
        Assert.Empty(strangerList!);
        Assert.NotEqual(note.Id, own.Id);
        Assert.Equal(1, await factory.WithDbAsync(db => db.Notes.CountAsync(n => n.UserId == strangerId)));
    }

    [LocalDatabaseFact]
    public async Task Concurrent_first_saves_end_with_one_note()
    {
        var (client, userId) = await CreateClientAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => SaveAsync(client, "2030-03-01", $"Wersja {i}")));

        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal(1, await factory.WithDbAsync(db => db.Notes.CountAsync(n => n.UserId == userId)));
    }

    private static Task<NoteResponse> SaveAsync(HttpClient client, string date, string content) =>
        ReadAsync<NoteResponse>(client.PutAsJsonAsync($"/notes/journal/{date}", new SaveNoteRequest("", content, null, null), MindflowApiFactory.Json));

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
