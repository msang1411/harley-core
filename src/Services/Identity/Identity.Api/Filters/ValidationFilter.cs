using FluentValidation;

namespace Identity.Api.Filters;

/// <summary>
/// Endpoint filter that validates the request argument of type <typeparamref name="T"/>
/// with its registered FluentValidation validator. On failure it short-circuits with a
/// 400 ValidationProblem, so the endpoint handler never runs with invalid input.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Model binding already ran, so the deserialized body is among the arguments.
        var request = context.Arguments.OfType<T>().FirstOrDefault();
        if (request is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<T>>();
        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (!result.IsValid)
        {
            return Results.ValidationProblem(result.ToDictionary());
        }

        return await next(context);
    }
}