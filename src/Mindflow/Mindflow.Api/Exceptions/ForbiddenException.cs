using System.Net;

namespace Mindflow.Api.Exceptions;

public class ForbiddenException(string message) : ApiException(message, (int)HttpStatusCode.Forbidden);
