using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mindflow.Api.Tests.Infrastructure;

public sealed class FakeTelegramHandler : HttpMessageHandler
{
    public List<(string Method, JsonObject Body)> Requests { get; } = [];
    public Queue<string> UpdatesResults { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
        lock (Requests) Requests.Add((method, body));
        var result = method == "getUpdates" ? (UpdatesResults.TryDequeue(out var next) ? next : "[]") : "true";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"ok":true,"result":{{result}}}""", Encoding.UTF8, "application/json") };
    }
}
