namespace Mindflow.Api.Services.Ai.Providers;

public static class AudioFormats
{
    private static readonly Dictionary<string, (string Mime, string Extension)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/webm"] = ("audio/webm", "webm"),
        ["video/webm"] = ("audio/webm", "webm"),
        ["audio/ogg"] = ("audio/ogg", "ogg"),
        ["audio/mp4"] = ("audio/mp4", "m4a"),
        ["video/mp4"] = ("audio/mp4", "m4a"),
        ["audio/m4a"] = ("audio/mp4", "m4a"),
        ["audio/x-m4a"] = ("audio/mp4", "m4a"),
        ["audio/aac"] = ("audio/aac", "aac"),
        ["audio/mpeg"] = ("audio/mpeg", "mp3"),
        ["audio/mp3"] = ("audio/mpeg", "mp3"),
        ["audio/wav"] = ("audio/wav", "wav"),
        ["audio/x-wav"] = ("audio/wav", "wav"),
        ["audio/wave"] = ("audio/wav", "wav"),
        ["audio/flac"] = ("audio/flac", "flac")
    };

    /// <summary>"audio/webm;codecs=opus" → "audio/webm"; null when the format is not accepted.</summary>
    public static string? Normalize(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var bare = contentType.Split(';', 2)[0].Trim();
        return Known.TryGetValue(bare, out var format) ? format.Mime : null;
    }

    public static string Extension(string mime) =>
        Known.TryGetValue(mime, out var format) ? format.Extension : "webm";
}
