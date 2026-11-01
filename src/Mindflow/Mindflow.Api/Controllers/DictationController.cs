using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Ai.Dictation;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("ai/dictation")]
[Authorize]
[EnableRateLimiting(AiRateLimits.Policy)]
public class DictationController(IDictationService dictationService) : ControllerBase
{
    private const long MaxUploadBytes = 9 * 1024 * 1024;

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> FromText([FromBody] DictationTextRequest request, CancellationToken ct)
        => Ok(await dictationService.ProcessAsync(new DictationInput(request.Text, null, null), ct));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> FromAudio(IFormFile audio, CancellationToken ct)
    {
        if (audio.Length == 0)
            return BadRequest(new { error = "Nagranie jest puste." });

        await using var stream = audio.OpenReadStream();
        using var buffer = new MemoryStream((int)audio.Length);
        await stream.CopyToAsync(buffer, ct);

        var input = new DictationInput(null, buffer.ToArray(), AudioFormats.Normalize(audio.ContentType));
        return Ok(await dictationService.ProcessAsync(input, ct));
    }
}
