namespace Mindflow.Api.Services.Telegram;

public record TelegramUpdate(long UpdateId, TelegramMessage? Message, TelegramCallbackQuery? CallbackQuery);
public record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, string? Text, TelegramVoice? Voice = null);
public record TelegramVoice(string FileId, int Duration, string? MimeType = null, long? FileSize = null);
public record TelegramFile(string FileId, string? FilePath, long? FileSize);
public record TelegramChat(long Id, string? Type = null);
public record TelegramUser(long Id, string? Username, string? FirstName);
public record TelegramCallbackQuery(string Id, TelegramUser From, TelegramMessage? Message, string? Data);
public record TelegramButton(string Text, string? CallbackData = null, string? Url = null);
