namespace Mindflow.Api.Services.GoogleCalendar;

/// <summary>Encrypts/decrypts OAuth tokens before they touch the database.</summary>
public interface IGoogleTokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
