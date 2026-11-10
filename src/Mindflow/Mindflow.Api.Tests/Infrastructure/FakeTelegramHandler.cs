using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mindflow.Api.Tests.Infrastructure;

public sealed class FakeTelegramHandler : HttpMessageHandler
{
    public List<Uri> Uris { get; } = [];
    public List<(string Method, JsonObject Body)> Requests { get; } = [];
    public Queue<string> UpdatesResults { get; } = new();
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public HashSet<string> FailingMethods { get; } = [];
    public Exception? Throw { get; set; }
    public bool Hang { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Throw is not null) throw Throw;
        if (Hang) await Task.Delay(Timeout.Infinite, ct);
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
        lock (Requests) Uris.Add(request.RequestUri!);
        lock (Requests) Requests.Add((method, body));
        var result = method == "getUpdates" ? (UpdatesResults.TryDequeue(out var next) ? next : "[]") : "true";
        return new HttpResponseMessage(FailingMethods.Contains(method) ? HttpStatusCode.Unauthorized : Status) { Content = new StringContent($$"""{"ok":true,"result":{{result}}}""", Encoding.UTF8, "application/json") };
    }
}
