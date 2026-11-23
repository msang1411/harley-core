using System.Security.Claims;
using Identity.Api.Extensions;
using Identity.Application.Auth;

namespace Identity.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, IAuthService auth) =>
        {
            var result = await auth.RegisterAsync(request);
            return result.IsSuccess ? Results.Created("/auth/me", result.Value) : result.ToProblem();
        }).WithValidation<RegisterRequest>();

        group.MapPost("/login", async (LoginRequest request, IAuthService auth) =>
        {
            var result = await auth.LoginAsync(request);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        }).WithValidation<LoginRequest>();

        group.MapPost("/refresh", async (RefreshRequest request, IAuthService auth) =>
        {
            var result = await auth.RefreshAsync(request);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        }).WithValidation<RefreshRequest>();

        group.MapPost("/logout", async (LogoutRequest request, IAuthService auth) =>
        {
            await auth.LogoutAsync(request);
            return Results.NoContent();
        }).WithValidation<LogoutRequest>();

        group.MapGet("/me", async (ClaimsPrincipal principal, IAuthService auth) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
                return Results.Unauthorized();

            var result = await auth.GetCurrentUserAsync(userId);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        }).RequireAuthorization();

        return app;
    }
}