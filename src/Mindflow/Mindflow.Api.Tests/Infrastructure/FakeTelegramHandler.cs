using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mindflow.Api.Tests.Infrastructure;

public sealed class FakeTelegramHandler : HttpMessageHandler
{
    public List<Uri> Uris { get; } = [];
    public List<(string Method, JsonObject Body)> Requests { get; } = [];
    public Queue<string> UpdatesResults { get; } = new();
    public Dictionary<string, (string Path, byte[] Bytes)> Files { get; } = new();
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public HashSet<string> FailingMethods { get; } = [];
    public Exception? Throw { get; set; }
    public bool Hang { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Throw is not null) throw Throw;
        if (Hang) await Task.Delay(Timeout.Infinite, ct);
        var segments = request.RequestUri!.AbsolutePath.Split('/');
        if (request.Method == HttpMethod.Get && segments is [_, "file", ..])
            return Download(request.RequestUri, string.Join('/', segments.Skip(3)));

        var method = segments.Last();
        var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
        lock (Requests) Uris.Add(request.RequestUri!);
        lock (Requests) Requests.Add((method, body));
        if (method == "getFile" && !Files.ContainsKey((string?)body["file_id"] ?? ""))
            return Json(HttpStatusCode.BadRequest, """{"ok":false,"description":"Bad Request: invalid file_id"}""");

        var result = method switch
        {
            "getUpdates" => UpdatesResults.TryDequeue(out var next) ? next : "[]",
            "getFile" => FileResult((string)body["file_id"]!),
            "sendMessage" or "editMessageReplyMarkup" => """{"message_id":1,"chat":{"id":1,"type":"private"}}""",
            _ => "true"
        };
        return Json(FailingMethods.Contains(method) ? HttpStatusCode.Unauthorized : Status, $$"""{"ok":true,"result":{{result}}}""");
    }

    private string FileResult(string fileId) =>
        JsonSerializer.Serialize(new { file_id = fileId, file_path = Files[fileId].Path, file_size = Files[fileId].Bytes.Length });

    private HttpResponseMessage Download(Uri uri, string path)
    {
        lock (Requests) Uris.Add(uri);
        var file = Files.Values.FirstOrDefault(f => f.Path == path);
        return file.Bytes is null
            ? Json(HttpStatusCode.NotFound, """{"ok":false}""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Bytes) };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
