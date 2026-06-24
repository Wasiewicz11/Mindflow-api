using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Channels;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("pomodoro")]
[Authorize]
public class PomodoroController(
    IPomodoroSessionService service,
    ICurrentUserService currentUserService,
    IPomodoroEventBroker eventBroker) : ControllerBase
{
    [HttpGet("session")]
    public async Task<IActionResult> Get()
    {
        var session = await service.GetAsync();
        return session is null ? NoContent() : Ok(session);
    }

    [HttpPut("session")]
    public async Task<IActionResult> Upsert([FromBody] UpsertPomodoroSessionRequest request) =>
        Ok(await service.UpsertAsync(request));

    [HttpDelete("session")]
    public async Task<IActionResult> Delete()
    {
        await service.DeleteAsync();
        return NoContent();
    }

    [HttpGet("events")]
    public async Task Events(CancellationToken ct)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        using var subscription = eventBroker.Subscribe(userId);

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");
        await Response.WriteAsync(": connected\n\n", ct);
        await Response.Body.FlushAsync(ct);

        var readTask = subscription.Events.ReadAsync(ct).AsTask();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var heartbeatTask = Task.Delay(TimeSpan.FromSeconds(15), ct);
                var completed = await Task.WhenAny(readTask, heartbeatTask);

                if (completed == readTask)
                {
                    var eventType = await readTask;
                    await Response.WriteAsync($"data: {eventType}\n\n", ct);
                    readTask = subscription.Events.ReadAsync(ct).AsTask();
                }
                else
                {
                    await Response.WriteAsync(": ping\n\n", ct);
                }

                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Klient zamknal dlugotrwale polaczenie SSE.
        }
        catch (ChannelClosedException)
        {
            // Subskrypcja zostala zakonczona podczas zamykania requestu.
        }
        catch (IOException)
        {
            // Klient rozlaczyl polaczenie zanim serwer zdazyl zapisac kolejny event.
        }
    }
}
