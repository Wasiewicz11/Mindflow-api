using System.Net;

namespace Mindflow.Api.Exceptions;

public class ServiceUnavailableException(string message) : ApiException(message, (int)HttpStatusCode.ServiceUnavailable);
