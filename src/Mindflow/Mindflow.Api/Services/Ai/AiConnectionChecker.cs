using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai;

public class AiConnectionChecker(GeminiApi gemini, OpenAiApi openAi) : IAiConnectionChecker
{
    public async Task CheckAsync(AiCredentials credentials, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            if (credentials.Provider == AiProvider.OpenAi)
                await openAi.CheckModelAsync(credentials.ApiKey, credentials.Model, timeout.Token);
            else
                await gemini.CheckModelAsync(credentials.ApiKey, credentials.Model, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw AiProviderErrors.Timeout();
        }
        catch (HttpRequestException)
        {
            throw new Exceptions.AiProviderException("Nie udało się połączyć z dostawcą AI.", StatusCodes.Status502BadGateway, retryable: true);
        }
    }
}
