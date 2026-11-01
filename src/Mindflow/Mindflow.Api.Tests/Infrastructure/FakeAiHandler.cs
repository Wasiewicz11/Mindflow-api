using System.Net;
using System.Text;

namespace Mindflow.Api.Tests.Infrastructure;

public sealed class FakeAiHandler : HttpMessageHandler
{
    private readonly object gate = new();
    private Func<HttpRequestMessage, string, HttpResponseMessage> respond = (_, _) => Json("{}");

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public void Respond(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        lock (gate) respond = responder;
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (gate)
        {
            Requests.Add((request, body));
            return respond(request, body);
        }
    }
}
