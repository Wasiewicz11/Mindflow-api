using System.Net;

namespace Mindflow.Api.Exceptions;

public class NotFoundException(string message) : ApiException(message, (int)HttpStatusCode.NotFound);
