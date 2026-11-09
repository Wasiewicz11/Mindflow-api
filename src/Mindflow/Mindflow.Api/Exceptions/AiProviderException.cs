namespace Mindflow.Api.Exceptions;

public class AiProviderException(string message, int statusCode, bool retryable = false)
    : ApiException(message, statusCode)
{
    public bool Retryable { get; } = retryable;
}
