using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai;

public enum AiKeySource
{
    User,
    Server
}

public record AiCredentials(AiProvider Provider, string ApiKey, string Model, AiKeySource Source);
