namespace Mindflow.Api.Services.GoogleCalendar;

/// <summary>
/// Signs/verifies the OAuth <c>state</c> parameter. It carries the user id through the
/// browser redirect (the callback has no auth header) and doubles as CSRF protection.
/// </summary>
public interface IOAuthStateProtector
{
    string Create(Guid userId);
    bool TryRead(string state, out Guid userId);
}
