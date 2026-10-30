using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class IntegrationDocsApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Docs_list_each_endpoint_once_with_its_scope()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateIntegrationClient(await factory.CreateManualTokenAsync(user.Id, IntegrationTokenScope.TasksRead));

        var docs = await client.GetFromJsonAsync<IntegrationDocsResponse>("/api/integration/v1/docs", MindflowApiFactory.Json);

        Assert.Single(docs!.Endpoints, endpoint => endpoint is { Method: "POST", Path: "/api/integration/v1/oauth/token" });
        var upsert = Assert.Single(docs.Endpoints, endpoint =>
            endpoint is { Method: "PUT", Path: "/api/integration/v1/tasks/external/{source}/{externalId}" });
        Assert.Contains("tasks:create", upsert.Scope);
        Assert.Contains("tasks:update", upsert.Scope);
    }
}
