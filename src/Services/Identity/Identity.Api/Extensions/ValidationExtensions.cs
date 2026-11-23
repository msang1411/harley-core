using Identity.Api.Filters;

namespace Identity.Api.Extensions;

public static class ValidationExtensions
{
    // Small helper so endpoints read as ".WithValidation<RegisterRequest>()".
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();
}