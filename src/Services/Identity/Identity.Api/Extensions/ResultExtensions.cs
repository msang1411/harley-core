using Identity.Application.Common;

namespace Identity.Api.Extensions;

public static class ResultExtensions
{
    public static IResult ToProblem<T>(this Result<T> result)
    {
        var first = result.Errors[0];

        return first.Type switch
        {
            ErrorType.Unauthorized => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: first.Description),
            ErrorType.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: first.Description),
            ErrorType.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: first.Description),
            _ => Results.ValidationProblem(
                result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()))
        };
    }
}