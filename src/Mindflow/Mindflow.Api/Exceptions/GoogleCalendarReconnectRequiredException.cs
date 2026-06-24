using System.Net;

namespace Mindflow.Api.Exceptions;

public class GoogleCalendarReconnectRequiredException()
    : ApiException(
        "Dostęp do Google Calendar wygasł. Połącz konto Google ponownie.",
        (int)HttpStatusCode.Conflict);
