using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Notes;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Notes;

public class NoteDictationApiTests(AiApiFactory factory) : IClassFixture<AiApiFactory>
{
    private const string Dictation = "/notes/dictation";
    private const string LiteNotice = "> Tryb Lite: dzienny limit lepszego modelu jest wykorzystany, więc notatka może być mniej dokładna.\n\n";

    [LocalDatabaseFact]
    public async Task Text_note_is_accepted_at_once_and_filled_in_by_the_processor()
    {
        var (client, userId) = await CreateClientAsync();
        var company = await factory.CreateProjectAsync(userId, "Firma");
        var yesterday = WarsawToday().AddDays(-1);
        factory.Ai.Respond((_, _) => Gemini(NoteJson(
            title: "Spotkanie z klientem",
            eventDate: yesterday.ToString("yyyy-MM-dd"),
            markdown: "## Co się wydarzyło\n- Omówiliśmy <b>budżet</b><br/>\n\n## Do zapamiętania\n- Klient <span class=\"x\">płaci</span> w euro",
            tasks: """[{"title":"Wysłać ofertę","priority":"P2","project":"Firma","subtasks":[]},{"title":"Zadzwonić do Ani","priority":"P3","project":null,"subtasks":["Ustalić termin"]}]""")));

        var response = await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Wczoraj spotkanie z klientem, płaci w euro."), MindflowApiFactory.Json);
        var accepted = await response.Content.ReadFromJsonAsync<NoteResponse>(MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(NoteStatus.Processing, accepted!.Status);
        Assert.Equal(NoteSource.Dictation, accepted.Source);
        Assert.Equal("Nowa notatka", accepted.Title);
        Assert.True(accepted.IsNew);
        var job = await FindJobAsync(accepted.Id);
        Assert.NotNull(job);
        Assert.Equal("Wczoraj spotkanie z klientem, płaci w euro.", job!.Text);
        Assert.Null(job.Audio);

        await ProcessAsync(accepted.Id);

        var note = await client.GetFromJsonAsync<NoteResponse>($"/notes/{accepted.Id}", MindflowApiFactory.Json);
        Assert.Equal(NoteStatus.Ready, note!.Status);
        Assert.Equal("Spotkanie z klientem", note.Title);
        Assert.Equal("## Co się wydarzyło\n- Omówiliśmy budżet\n\n## Do zapamiętania\n- Klient płaci w euro", note.Content);
        Assert.Equal(["praca", "klient"], note.Tags);
        Assert.Equal(yesterday, note.EventDate);
        Assert.Equal("Wczoraj spotkanie z klientem, płaci w euro.", note.Transcript);
        Assert.True(note.IsNew);
        Assert.Null(await FindJobAsync(accepted.Id));
        var suggestions = await SuggestionsAsync(accepted.Id);
        Assert.Equal(["Wysłać ofertę", "Zadzwonić do Ani"], suggestions.Select(s => s.Title));
        Assert.Equal([0, 1], suggestions.Select(s => s.Order));
        Assert.All(suggestions, s => Assert.Equal(NoteSuggestionStatus.Pending, s.Status));
        Assert.Equal(company.Id, suggestions[0].ProjectId);
        Assert.Equal(TaskPriority.P2, suggestions[0].Priority);
        Assert.Equal(["Ustalić termin"], suggestions[1].Subtasks);
    }

    [LocalDatabaseFact]
    public async Task Recording_is_kept_only_until_the_note_is_processed()
    {
        var (client, _) = await CreateClientAsync();
        string? sentBody = null;
        factory.Ai.Respond((_, body) =>
        {
            sentBody = body;
            return Gemini(NoteJson(transcript: "Jutro kupić mleko.", title: "Zakupy", tasks: "[]"));
        });

        var response = await client.PostAsync(Dictation, Audio("audio/webm;codecs=opus"));
        var accepted = await response.Content.ReadFromJsonAsync<NoteResponse>(MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await FindJobAsync(accepted!.Id);
        Assert.Equal(2048, job!.Audio!.Length);
        Assert.Equal("audio/webm", job.AudioMimeType);
        Assert.Null(job.Text);

        await ProcessAsync(accepted.Id);

        using var doc = JsonDocument.Parse(sentBody!);
        Assert.Equal("audio/webm", doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("inline_data").GetProperty("mime_type").GetString());
        Assert.Null(await FindJobAsync(accepted.Id));
        var note = await client.GetFromJsonAsync<NoteResponse>($"/notes/{accepted.Id}", MindflowApiFactory.Json);
        Assert.Equal(NoteStatus.Ready, note!.Status);
        Assert.Equal("Jutro kupić mleko.", note.Transcript);
    }

    [LocalDatabaseFact]
    public async Task Notes_share_the_daily_dictation_limit_and_switch_to_lite()
    {
        var (client, userId) = await CreateClientAsync();
        var paths = new List<string>();
        factory.Ai.Respond((request, _) =>
        {
            lock (paths) paths.Add(request.RequestUri!.AbsolutePath);
            return Gemini(NoteJson(title: "Notatka", markdown: "Treść", tasks: "[]"));
        });

        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync("/ai/dictation", new DictationTextRequest("Kupić mleko"), MindflowApiFactory.Json)).EnsureSuccessStatusCode();
        var notes = new List<NoteResponse>();
        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            notes.Add((await response.Content.ReadFromJsonAsync<NoteResponse>(MindflowApiFactory.Json))!);
        }
        var blocked = await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("limit", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(2, await factory.WithDbAsync(db => db.Notes.CountAsync(n => n.UserId == userId)));
        foreach (var note in notes)
        {
            Assert.True((await FindJobAsync(note.Id))!.Lite);
            await ProcessAsync(note.Id);
            var ready = await client.GetFromJsonAsync<NoteResponse>($"/notes/{note.Id}", MindflowApiFactory.Json);
            Assert.Equal(LiteNotice + "Treść", ready!.Content);
        }
        Assert.All(paths.Skip(3), p => Assert.Contains("gemini-3.5-flash-lite:", p));
        Assert.Equal(5, await DictationCallsAsync(userId));
    }

    [LocalDatabaseFact]
    public async Task Failed_note_can_be_retried_without_counting_the_limit_up_to_three_attempts()
    {
        var (client, userId) = await CreateClientAsync();
        factory.Ai.Respond((_, _) => FakeAiHandler.Json("""{"error":{"message":"overloaded"}}""", HttpStatusCode.InternalServerError));
        var accepted = await ReadAsync<NoteResponse>(client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json));

        await ProcessAsync(accepted.Id);

        var failed = await client.GetFromJsonAsync<NoteResponse>($"/notes/{accepted.Id}", MindflowApiFactory.Json);
        Assert.Equal(NoteStatus.Failed, failed!.Status);
        Assert.Equal("Notatka", failed.Transcript);
        Assert.Equal(1, (await FindJobAsync(accepted.Id))!.Attempts);

        var retried = await ReadAsync<NoteResponse>(client.PostAsync($"/notes/{accepted.Id}/retry", null));
        Assert.Equal(NoteStatus.Processing, retried.Status);
        Assert.Equal(1, await DictationCallsAsync(userId));

        factory.Ai.Respond((_, _) => Gemini(NoteJson(title: "Udana", markdown: "Treść", tasks: "[]")));
        await ProcessAsync(accepted.Id);
        var ready = await client.GetFromJsonAsync<NoteResponse>($"/notes/{accepted.Id}", MindflowApiFactory.Json);
        Assert.Equal(NoteStatus.Ready, ready!.Status);
        Assert.Equal("Udana", ready.Title);

        factory.Ai.Respond((_, _) => FakeAiHandler.Json("{}", HttpStatusCode.InternalServerError));
        var other = await ReadAsync<NoteResponse>(client.PostAsJsonAsync(Dictation, new DictationTextRequest("Druga"), MindflowApiFactory.Json));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (attempt > 1) (await client.PostAsync($"/notes/{other.Id}/retry", null)).EnsureSuccessStatusCode();
            await ProcessAsync(other.Id);
        }
        var exhausted = await client.PostAsync($"/notes/{other.Id}/retry", null);

        Assert.Equal(HttpStatusCode.BadRequest, exhausted.StatusCode);
        using var error = JsonDocument.Parse(await exhausted.Content.ReadAsStringAsync());
        Assert.Equal("Tej notatki nie można już przetworzyć ponownie.", error.RootElement.GetProperty("error").GetString());
        Assert.Equal(3, (await FindJobAsync(other.Id))!.Attempts);
        var ready2 = await client.PostAsync($"/notes/{accepted.Id}/retry", null);
        Assert.Equal(HttpStatusCode.BadRequest, ready2.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Result_that_cannot_be_saved_fails_the_note_instead_of_leaving_it_processing()
    {
        var (client, _) = await CreateClientAsync();
        factory.Ai.Respond((_, _) => Gemini(NoteJson(title: "Notatka", markdown: "Tre\u0000ść", tasks: "[]")));
        var accepted = await ReadAsync<NoteResponse>(client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json));

        await ProcessAsync(accepted.Id);

        var note = await client.GetFromJsonAsync<NoteResponse>($"/notes/{accepted.Id}", MindflowApiFactory.Json);
        Assert.Equal(NoteStatus.Failed, note!.Status);
        Assert.Equal(1, (await FindJobAsync(accepted.Id))!.Attempts);
        Assert.Empty(await SuggestionsAsync(accepted.Id));
        (await client.PostAsync($"/notes/{accepted.Id}/retry", null)).EnsureSuccessStatusCode();
    }

    [LocalDatabaseFact]
    public async Task Suggestion_drops_a_project_deleted_while_the_note_was_processed()
    {
        var (client, userId) = await CreateClientAsync();
        var project = await factory.CreateProjectAsync(userId, "Firma");
        factory.Ai.Respond((_, _) =>
        {
            factory.WithDbAsync(db => db.Projects.Where(p => p.Id == project.Id).ExecuteDeleteAsync()).GetAwaiter().GetResult();
            return Gemini(NoteJson(title: "Notatka", markdown: "Treść", tasks: """[{"title":"Wysłać ofertę","priority":"P2","project":"Firma","subtasks":[]}]"""));
        });
        var accepted = await ReadAsync<NoteResponse>(client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json));

        await ProcessAsync(accepted.Id);

        var note = await client.GetFromJsonAsync<NoteResponse>($"/notes/{accepted.Id}", MindflowApiFactory.Json);
        Assert.Equal(NoteStatus.Ready, note!.Status);
        var suggestion = Assert.Single(await SuggestionsAsync(accepted.Id));
        Assert.Null(suggestion.ProjectId);
    }

    [LocalDatabaseFact]
    public async Task Third_failure_drops_the_recording_and_an_exhausted_job_is_not_sent_again()
    {
        var (client, _) = await CreateClientAsync();
        factory.Ai.Respond((_, _) => FakeAiHandler.Json("{}", HttpStatusCode.InternalServerError));
        var accepted = await ReadAsync<NoteResponse>(client.PostAsync(Dictation, Audio("audio/webm")));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.NotNull((await FindJobAsync(accepted.Id))!.Audio);
            if (attempt > 1) (await client.PostAsync($"/notes/{accepted.Id}/retry", null)).EnsureSuccessStatusCode();
            await ProcessAsync(accepted.Id);
        }

        var job = await FindJobAsync(accepted.Id);
        Assert.Equal(3, job!.Attempts);
        Assert.Null(job.Audio);

        await factory.WithDbAsync(db => db.Notes.Where(n => n.Id == accepted.Id)
            .ExecuteUpdateAsync(n => n.SetProperty(x => x.Status, NoteStatus.Processing)));
        var requests = factory.Ai.Requests.Count;
        await ProcessAsync(accepted.Id);

        var note = await client.GetFromJsonAsync<NoteResponse>($"/notes/{accepted.Id}", MindflowApiFactory.Json);
        Assert.Equal(NoteStatus.Failed, note!.Status);
        Assert.Equal(requests, factory.Ai.Requests.Count);
        Assert.Equal(3, (await FindJobAsync(accepted.Id))!.Attempts);
    }

    [LocalDatabaseFact]
    public async Task Note_cannot_be_edited_while_it_is_processed()
    {
        var (client, _) = await CreateClientAsync();
        var accepted = await ReadAsync<NoteResponse>(client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json));

        var response = await client.PutAsJsonAsync($"/notes/{accepted.Id}", new SaveNoteRequest("Tytuł", "Treść", null, null), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Notatka jest jeszcze przetwarzana.", error.RootElement.GetProperty("error").GetString());
    }

    [LocalDatabaseFact]
    public async Task Marking_a_processing_note_as_reviewed_changes_nothing()
    {
        var (client, _) = await CreateClientAsync();
        var accepted = await ReadAsync<NoteResponse>(client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json));

        var response = await ReadAsync<NoteResponse>(client.PostAsync($"/notes/{accepted.Id}/review", null));

        Assert.Equal(NoteStatus.Processing, response.Status);
        Assert.True(response.IsNew);
        Assert.Null(await factory.WithDbAsync(db => Task.FromResult(db.Notes.Single(n => n.Id == accepted.Id).ReviewedAt)));
    }

    [LocalDatabaseFact]
    public async Task Retry_is_owner_only_and_deleting_the_note_removes_its_job_and_suggestions()
    {
        var (client, _) = await CreateClientAsync();
        var (stranger, _) = await CreateClientAsync();
        var accepted = await ReadAsync<NoteResponse>(client.PostAsJsonAsync(Dictation, new DictationTextRequest("Notatka"), MindflowApiFactory.Json));
        await factory.WithDbAsync(async db =>
        {
            db.NoteTaskSuggestions.Add(new NoteTaskSuggestion
            {
                Id = Guid.NewGuid(),
                NoteId = accepted.Id,
                Title = "Zadanie",
                Priority = TaskPriority.P3,
                Status = NoteSuggestionStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/notes/{accepted.Id}/retry", null)).StatusCode);

        (await client.DeleteAsync($"/notes/{accepted.Id}")).EnsureSuccessStatusCode();

        Assert.Null(await FindJobAsync(accepted.Id));
        Assert.Empty(await SuggestionsAsync(accepted.Id));
    }

    [LocalDatabaseFact]
    public async Task Dictation_requires_input_and_a_signed_in_user()
    {
        var (client, _) = await CreateClientAsync();
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Dictation, new { text = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync(Dictation, Audio("image/png"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Dictation, new { text = "Notatka" })).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Worker_picks_up_jobs_left_from_before_the_restart()
    {
        var user = await factory.CreateUserAsync();
        var noteId = Guid.NewGuid();
        await factory.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Notes.Add(new Note
            {
                Id = noteId,
                UserId = user.Id,
                Title = "Nowa notatka",
                Source = NoteSource.Dictation,
                Status = NoteStatus.Processing,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.NoteDictationJobs.Add(new NoteDictationJob { NoteId = noteId, UserId = user.Id, Text = "Notatka", CreatedAt = now });
            await db.SaveChangesAsync();
        });

        await using var restarted = new WorkerApiFactory();
        restarted.Ai.Respond((_, _) => Gemini(NoteJson(title: "Po restarcie", markdown: "Treść", tasks: "[]")));
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), s => s is NoteDictationWorker);
        Assert.Contains(restarted.Services.GetServices<IHostedService>(), s => s is NoteDictationWorker);

        var status = NoteStatus.Processing;
        for (var waited = 0; waited < 5000 && status != NoteStatus.Ready; waited += 100)
        {
            await Task.Delay(100);
            status = await factory.WithDbAsync(db => db.Notes.Where(n => n.Id == noteId).Select(n => n.Status).SingleAsync());
        }

        Assert.Equal(NoteStatus.Ready, status);
        Assert.Null(await FindJobAsync(noteId));
    }

    private sealed class WorkerApiFactory : AiApiFactory
    {
        protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>(base.Settings)
        {
            ["Notes:Dictation:WorkerEnabled"] = "true"
        };
    }

    private async Task ProcessAsync(Guid noteId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NoteDictationProcessor>().ProcessAsync(noteId, CancellationToken.None);
    }

    private Task<NoteDictationJob?> FindJobAsync(Guid noteId) =>
        factory.WithDbAsync(db => db.NoteDictationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.NoteId == noteId));

    private Task<List<NoteTaskSuggestion>> SuggestionsAsync(Guid noteId) =>
        factory.WithDbAsync(db => db.NoteTaskSuggestions.AsNoTracking().Where(s => s.NoteId == noteId).OrderBy(s => s.Order).ToListAsync());

    private Task<int> DictationCallsAsync(Guid userId) =>
        factory.WithDbAsync(db => db.AiUsageDaily.Where(u => u.UserId == userId).SumAsync(u => u.DictationCalls));

    private static DateOnly WarsawToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).DateTime);

    private static string NoteJson(string title, string tasks, string transcript = "", string? eventDate = null, string markdown = "") =>
        JsonSerializer.Serialize(new
        {
            transcript,
            title,
            eventDate,
            tags = new[] { "Praca", "#Klient" },
            markdown,
            tasks = JsonDocument.Parse(tasks).RootElement
        });

    private static HttpResponseMessage Gemini(string json) =>
        FakeAiHandler.Json(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } } }));

    private static MultipartFormDataContent Audio(string contentType)
    {
        var file = new ByteArrayContent(new byte[2048]);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { file, "audio", "dictation.webm" } };
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
