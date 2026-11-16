using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Notes;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("notes/journal")]
[Authorize]
public class JournalController(IJournalService journal) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JournalDayResponse>>> List([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await journal.ListAsync(from, to, ct));

    [HttpGet("{date}")]
    public async Task<ActionResult<NoteResponse>> Get(DateOnly date, CancellationToken ct) =>
        await journal.GetAsync(date, ct) is { } note ? Ok(note) : NoContent();

    [HttpPut("{date}")]
    public async Task<ActionResult<NoteResponse>> Save(DateOnly date, SaveNoteRequest request, CancellationToken ct) =>
        await journal.SaveAsync(date, request, ct) is { } note ? Ok(note) : NoContent();
}
