using TelegramGateway.Api.Contracts;

namespace TelegramGateway.Api.Http;

public static class ApiErrors
{
    public static IResult Error(int statusCode, string message) => Results.Json(new ApiErrorResponse(message), statusCode: statusCode);
    public static IResult BadRequest(string message) => Error(400, message);
    public static IResult Conflict(string message) => Error(409, message);
}
