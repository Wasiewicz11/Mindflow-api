namespace Mindflow.Api.Exceptions;

public class TooManyRequestsException(string message) : ApiException(message, StatusCodes.Status429TooManyRequests);
