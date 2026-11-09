namespace Mindflow.Api.Services.Ai;

public interface IAiConnectionChecker
{
    Task CheckAsync(AiCredentials credentials, CancellationToken ct = default);
}
