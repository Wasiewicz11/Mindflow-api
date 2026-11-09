using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Ai;

public class AiSettingsApiTests(AiApiFactory factory) : IClassFixture<AiApiFactory>
{
    private const string Settings = "/ai/settings";

    [LocalDatabaseFact]
    public async Task New_user_runs_on_the_server_key()
    {
        var (client, _) = await CreateClientAsync();

        var settings = await client.GetFromJsonAsync<AiSettingsResponse>(Settings, MindflowApiFactory.Json);

        Assert.Equal(AiProvider.Gemini, settings!.Provider);
        Assert.Equal("gemini-3.5-flash", settings.Model);
        Assert.False(settings.HasKey);
        Assert.True(settings.ServerKeyAvailable);
        Assert.Equal("server", settings.ActiveSource);
        Assert.Equal(3, settings.Dictation.Limit);
    }

    [LocalDatabaseFact]
    public async Task Saved_key_is_encrypted_and_only_its_last_four_characters_come_back()
    {
        var (client, userId) = await CreateClientAsync();

        var response = await client.PutAsJsonAsync(Settings, new UpdateAiSettingsRequest(AiProvider.Gemini, "AIzaSyUserKey-9876", null), MindflowApiFactory.Json);
        var body = await response.Content.ReadAsStringAsync();
        var stored = await factory.WithDbAsync(db => db.UserAiSettings.AsNoTracking().SingleAsync(s => s.UserId == userId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("AIzaSyUserKey", body);
        Assert.Contains("\"keyLast4\":\"9876\"", body);
        Assert.Contains("\"activeSource\":\"user\"", body);
        Assert.StartsWith("v1:", stored.ApiKeyEncrypted);
        Assert.DoesNotContain("AIzaSyUserKey", stored.ApiKeyEncrypted);
    }

    [LocalDatabaseFact]
    public async Task Switching_provider_without_a_new_key_drops_the_old_key()
    {
        var (client, _) = await CreateClientAsync();
        await client.PutAsJsonAsync(Settings, new UpdateAiSettingsRequest(AiProvider.Gemini, "AIzaSyUserKey-9876", null), MindflowApiFactory.Json);

        var response = await client.PutAsJsonAsync(Settings, new UpdateAiSettingsRequest(AiProvider.OpenAi, null, "gpt-4o"), MindflowApiFactory.Json);
        var settings = await response.Content.ReadFromJsonAsync<AiSettingsResponse>(MindflowApiFactory.Json);

        Assert.Equal(AiProvider.OpenAi, settings!.Provider);
        Assert.Equal("gpt-4o", settings.Model);
        Assert.False(settings.HasKey);
        Assert.Equal("server", settings.ActiveSource);
    }

    [LocalDatabaseFact]
    public async Task Deleting_the_key_falls_back_to_the_server_key()
    {
        var (client, _) = await CreateClientAsync();
        await client.PutAsJsonAsync(Settings, new UpdateAiSettingsRequest(AiProvider.Gemini, "AIzaSyUserKey-9876", null), MindflowApiFactory.Json);

        var response = await client.DeleteAsync($"{Settings}/key");
        var settings = await response.Content.ReadFromJsonAsync<AiSettingsResponse>(MindflowApiFactory.Json);

        Assert.False(settings!.HasKey);
        Assert.Null(settings.KeyLast4);
        Assert.Equal("server", settings.ActiveSource);
    }

    [LocalDatabaseFact]
    public async Task Invalid_model_name_is_rejected()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.PutAsJsonAsync(Settings, new UpdateAiSettingsRequest(AiProvider.Gemini, null, "../models/x"), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Connection_test_checks_the_typed_key_against_the_provider()
    {
        var (client, _) = await CreateClientAsync();
        factory.Ai.Respond((request, _) => request.Headers.GetValues("x-goog-api-key").Single() == "AIzaSyTyped-0001"
            ? FakeAiHandler.Json("""{"name":"models/gemini-3.5-flash"}""")
            : FakeAiHandler.Json("""{"error":{"message":"API key not valid"}}""", HttpStatusCode.BadRequest));

        var ok = await PostTestAsync(client, new TestAiConnectionRequest(AiProvider.Gemini, "AIzaSyTyped-0001", null));
        var bad = await PostTestAsync(client, new TestAiConnectionRequest(AiProvider.Gemini, "AIzaSyWrong-0002", null));

        Assert.True(ok.Ok);
        Assert.Equal("gemini-3.5-flash", ok.Model);
        Assert.False(bad.Ok);
        Assert.Contains("klucz", bad.Message);
    }

    [LocalDatabaseFact]
    public async Task Connection_test_without_a_typed_key_uses_the_active_credentials()
    {
        var (client, _) = await CreateClientAsync();
        factory.Ai.Respond((request, _) => FakeAiHandler.Json(request.Headers.GetValues("x-goog-api-key").Single() == AiApiFactory.ServerGeminiKey ? "{}" : "{\"error\":{}}",
            request.Headers.GetValues("x-goog-api-key").Single() == AiApiFactory.ServerGeminiKey ? HttpStatusCode.OK : HttpStatusCode.Forbidden));

        var result = await PostTestAsync(client, new TestAiConnectionRequest(null, null, null));

        Assert.True(result.Ok);
        Assert.Equal("server", result.Source);
    }

    [LocalDatabaseFact]
    public async Task Settings_require_a_signed_in_user()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Settings)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"{Settings}/test", new { })).StatusCode);
    }

    private static async Task<AiConnectionTestResponse> PostTestAsync(HttpClient client, TestAiConnectionRequest request)
    {
        var response = await client.PostAsJsonAsync($"{Settings}/test", request, MindflowApiFactory.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AiConnectionTestResponse>(MindflowApiFactory.Json))!;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync()
    {
        var user = await factory.CreateUserAsync();
        return (factory.CreateUserClient(user.Id), user.Id);
    }
}
