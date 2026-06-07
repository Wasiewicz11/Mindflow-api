using Mindflow.Api.Models;

namespace Mindflow.Api.Services.Ai;

public interface ISuggestionActionExecutor
{
    Task ExecuteAsync(SuggestionAction action, CancellationToken ct = default);
}
