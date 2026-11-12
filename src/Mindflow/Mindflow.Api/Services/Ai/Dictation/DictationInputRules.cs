using Mindflow.Api.Exceptions;

namespace Mindflow.Api.Services.Ai.Dictation;

public static class DictationInputRules
{
    public static string CleanText(string? text) => (text ?? "").Replace("\0", "").Trim();

    public static void Validate(DictationInput input, DictationOptions options)
    {
        if (input.IsAudio)
        {
            if (input.Audio!.Length > options.MaxAudioBytes)
                throw new AiProviderException("Nagranie jest za długie.", StatusCodes.Status413PayloadTooLarge);
            if (input.AudioMimeType is null)
                throw new AiProviderException("Nieobsługiwany format nagrania.", StatusCodes.Status415UnsupportedMediaType);
            return;
        }

        var text = CleanText(input.Text);
        if (string.IsNullOrEmpty(text))
            throw new BadRequestException("Nagraj albo wpisz notatkę.");
        if (text.Length > options.MaxTextLength)
            throw new BadRequestException($"Notatka może mieć najwyżej {options.MaxTextLength} znaków.");
    }
}
