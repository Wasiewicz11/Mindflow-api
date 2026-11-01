using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Ai;

public class DictationApiTests(AiApiFactory factory) : IClassFixture<AiApiFactory>
{
    private const string Dictation = "/ai/dictation";

    [LocalDatabaseFact]
    public async Task Text_note_becomes_tasks_mapped_to_the_users_projects()
    {
        var (client, userId) = await CreateClientAsync();
        var company = await factory.CreateProjectAsync(userId, "Firma");
        string? sentBody = null;
        factory.Ai.Respond((_, body) =>
        {
            sentBody = body;
            return Gemini("""{"transcript":"","tasks":[{"title":"Wysłać umowę","priority":"P2","project":"Firma","dueDate":null,"subtasks":[]},{"title":"Kupić mleko","priority":"P3","project":"Nieznany","subtasks":[]}]}""");
        });

        var response = await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Sprawy firmowe: wysłać umowę. Kupić mleko."), MindflowApiFactory.Json);
        var result = await response.Content.ReadFromJsonAsync<DictationResponse>(MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Sprawy firmowe: wysłać umowę. Kupić mleko.", result!.Transcript);
        Assert.Equal("server", result.Source);
        Assert.Equal(company.Id, result.Tasks[0].ProjectId);
        Assert.Null(result.Tasks[1].ProjectId);
        Assert.Contains("\\u0022Firma\\u0022", sentBody);
        Assert.DoesNotContain(company.Id.ToString(), sentBody);
        Assert.Equal(1, result.Usage.Used);
    }

    [LocalDatabaseFact]
    public async Task Audio_upload_is_sent_inline_with_its_normalized_type()
    {
        var (client, _) = await CreateClientAsync();
        string? sentBody = null;
        factory.Ai.Respond((_, body) =>
        {
            sentBody = body;
            return Gemini("""{"transcript":"Jutro kupić mleko.","tasks":[{"title":"Kupić mleko","priority":"P3","subtasks":[]}]}""");
        });

        var response = await client.PostAsync(Dictation, Audio("audio/webm;codecs=opus"));
        var result = await response.Content.ReadFromJsonAsync<DictationResponse>(MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Jutro kupić mleko.", result!.Transcript);
        using var doc = JsonDocument.Parse(sentBody!);
        Assert.Equal("audio/webm", doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("inline_data").GetProperty("mime_type").GetString());
    }

    [LocalDatabaseFact]
    public async Task Unsupported_audio_type_is_rejected_before_calling_ai()
    {
        var (client, _) = await CreateClientAsync();
        var before = factory.Ai.Requests.Count;

        var response = await client.PostAsync(Dictation, Audio("image/png"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(before, factory.Ai.Requests.Count);
    }

    [LocalDatabaseFact]
    public async Task Daily_limit_on_the_server_key_stops_further_calls()
    {
        var (client, _) = await CreateClientAsync();
        factory.Ai.Respond((_, _) => Gemini("""{"transcript":"","tasks":[]}"""));

        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Kupić mleko"), MindflowApiFactory.Json)).EnsureSuccessStatusCode();
        var blocked = await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Kupić mleko"), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("limit", await blocked.Content.ReadAsStringAsync());
    }

    [LocalDatabaseFact]
    public async Task Own_key_is_used_instead_of_the_server_key()
    {
        var (client, _) = await CreateClientAsync();
        await client.PutAsJsonAsync("/ai/settings", new UpdateAiSettingsRequest(AiProvider.Gemini, "AIzaSyOwnKey-4242", "gemini-3.8-flash"), MindflowApiFactory.Json);
        string? usedKey = null;
        string? path = null;
        factory.Ai.Respond((request, _) =>
        {
            usedKey = request.Headers.GetValues("x-goog-api-key").Single();
            path = request.RequestUri!.AbsolutePath;
            return Gemini("""{"transcript":"","tasks":[]}""");
        });

        var result = await (await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Kupić mleko"), MindflowApiFactory.Json))
            .Content.ReadFromJsonAsync<DictationResponse>(MindflowApiFactory.Json);

        Assert.Equal("AIzaSyOwnKey-4242", usedKey);
        Assert.Contains("gemini-3.8-flash", path);
        Assert.Equal("user", result!.Source);
        Assert.Equal(200, result.Usage.Limit);
    }

    [LocalDatabaseFact]
    public async Task Rejected_key_is_reported_as_unprocessable()
    {
        var (client, _) = await CreateClientAsync();
        factory.Ai.Respond((_, _) => FakeAiHandler.Json("""{"error":{"message":"API key not valid"}}""", HttpStatusCode.BadRequest));

        var response = await client.PostAsJsonAsync(Dictation, new DictationTextRequest("Kupić mleko"), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Empty_or_too_long_text_is_a_bad_request()
    {
        var (client, _) = await CreateClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Dictation, new { text = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Dictation, new { text = new string('a', 5001) })).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Dictation_requires_a_signed_in_user()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Dictation, new { text = "Kupić mleko" })).StatusCode);
    }

    private static HttpResponseMessage Gemini(string json) =>
        FakeAiHandler.Json(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } } }));

    private static MultipartFormDataContent Audio(string contentType)
    {
        var file = new ByteArrayContent(new byte[2048]);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { file, "audio", "dictation.webm" } };
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync()
    {
        var user = await factory.CreateUserAsync();
        return (factory.CreateUserClient(user.Id), user.Id);
    }
}
