using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Ai.Dictation;

public class DictationQuota(IAiUsageRepository usageRepository, IOptions<AiOptions> options) : IDictationQuota
{
    private readonly DictationOptions _options = options.Value.Dictation;

    public async Task<DictationReservation> ReserveAsync(Guid userId, AiCredentials credentials, CancellationToken ct)
    {
        var limit = credentials.Source == AiKeySource.User ? _options.OwnKeyDailyLimit : _options.DailyLimit;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var used = await usageRepository.GetDictationCallsAsync(userId, today, ct);
        var lite = used >= limit;
        if (lite)
        {
            if (!CanUseLite(credentials) || used >= limit + _options.LiteDailyLimit)
                throw new TooManyRequestsException($"Wykorzystano dzienny limit dyktowania ({limit}). Spróbuj jutro albo dodaj własny klucz w Ustawieniach → AI.");
            credentials = credentials with { Model = _options.GeminiFallbackModel };
        }
        await usageRepository.IncrementDictationCallsAsync(userId, today, ct);

        return new DictationReservation(credentials, lite, new AiUsageResponse(used + 1, limit));
    }

    private bool CanUseLite(AiCredentials credentials) =>
        credentials.Provider == AiProvider.Gemini
        && _options.LiteDailyLimit > 0
        && !string.IsNullOrWhiteSpace(_options.GeminiFallbackModel);
}
