using System.Net;

namespace Mindflow.Api.Exceptions;

public class BadRequestException(string message) : ApiException(message, (int)HttpStatusCode.BadRequest);
