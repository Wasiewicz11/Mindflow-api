using System.Net;

namespace Mindflow.Api.Exceptions;

public class UnauthorizedException(string message) : ApiException(message, (int)HttpStatusCode.Unauthorized);
