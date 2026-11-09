using System.Net;
using Mindflow.Api.Exceptions;

namespace Mindflow.Api.Services.Ai.Providers;

public static class AiProviderErrors
{
    public static AiProviderException FromStatus(HttpStatusCode status, string body, string model) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new("Dostawca AI odrzucił klucz API. Sprawdź klucz w Ustawieniach → AI.", StatusCodes.Status422UnprocessableEntity),
        HttpStatusCode.BadRequest when body.Contains("API key", StringComparison.OrdinalIgnoreCase) || body.Contains("API_KEY", StringComparison.Ordinal) =>
            new("Dostawca AI odrzucił klucz API. Sprawdź klucz w Ustawieniach → AI.", StatusCodes.Status422UnprocessableEntity),
        HttpStatusCode.NotFound =>
            new($"Model „{model}” nie jest dostępny dla tego klucza.", StatusCodes.Status422UnprocessableEntity, retryable: true),
        HttpStatusCode.TooManyRequests =>
            new("Wyczerpano limit u dostawcy AI. Spróbuj za chwilę.", StatusCodes.Status429TooManyRequests, retryable: true),
        HttpStatusCode.BadRequest =>
            new("Dostawca AI nie przyjął nagrania albo tekstu.", StatusCodes.Status422UnprocessableEntity),
        _ when (int)status >= 500 =>
            new("Dostawca AI jest chwilowo przeciążony. Spróbuj ponownie za chwilę.", StatusCodes.Status503ServiceUnavailable, retryable: true),
        _ => new("Dostawca AI zwrócił nieoczekiwany błąd.", StatusCodes.Status502BadGateway)
    };

    public static AiProviderException Timeout() =>
        new("AI nie odpowiedziało na czas. Spróbuj krótszego nagrania.", StatusCodes.Status504GatewayTimeout, retryable: true);

    public static AiProviderException EmptyResponse() =>
        new("AI nie zwróciło odpowiedzi. Spróbuj ponownie.", StatusCodes.Status502BadGateway, retryable: true);
}
