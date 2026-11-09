using System.Net.Http.Json;
using System.Text.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Ai;

public class SuggestionsAiApiTests(AiApiFactory factory) : IClassFixture<AiApiFactory>
{
    [LocalDatabaseFact]
    public async Task Generating_suggestions_uses_the_users_own_key_and_its_higher_limit()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateUserClient(user.Id);
        var task = await (await client.PostAsJsonAsync("/tasks", new { content = "Raport kwartalny", priority = "P2", dueDate = "2026-10-20" }, MindflowApiFactory.Json))
            .Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json);
        await client.PutAsJsonAsync("/ai/settings", new UpdateAiSettingsRequest(AiProvider.Gemini, "AIzaSyOwnKey-7777", null), MindflowApiFactory.Json);

        string? key = null;
        string? sent = null;
        factory.Ai.Respond((request, body) =>
        {
            key = request.Headers.GetValues("x-goog-api-key").Single();
            sent = body;
            var answer = """{"suggestions":[{"title":"Domknij raport","body":"Raport jest po terminie.","actions":[{"taskRef":1,"actionType":"ChangePriority","payload":{"priority":"P1"}}]}]}""";
            return FakeAiHandler.Json(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = answer } } } } } }));
        });

        var result = await (await client.PostAsync("/suggestions/generate", null)).Content.ReadFromJsonAsync<GenerateSuggestionsResponse>(MindflowApiFactory.Json);
        var pending = await client.GetFromJsonAsync<List<SuggestionResponse>>("/suggestions", MindflowApiFactory.Json);

        Assert.Equal("ai", result!.Mode);
        Assert.Equal(20, result.AiLimit);
        Assert.Equal("AIzaSyOwnKey-7777", key);
        Assert.DoesNotContain(task!.Id.ToString(), sent);
        Assert.Equal("Domknij raport", Assert.Single(pending!).Title);
        Assert.Equal(task.Id, pending![0].Actions.Single().TaskId);
    }
}
