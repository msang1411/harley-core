using System.Text.Json.Serialization;
using Identity.Infrastructure.Authentication;
using Microsoft.Extensions.Options;

namespace Identity.Api.Endpoints;

public static class WellKnownEndpoints
{
    public static IEndpointRouteBuilder MapWellKnownEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/jwks.json", (JwtTokenService tokenService) =>
            Results.Ok(new { keys = new[] { tokenService.PublicJwk } }));

        app.MapGet("/.well-known/openid-configuration", (HttpRequest request, IOptions<JwtOptions> jwt) =>
        {
            var jwksUri = $"{request.Scheme}://{request.Host}/.well-known/jwks.json";
            return Results.Ok(new OpenIdConfiguration(jwt.Value.Issuer, jwksUri, ["RS256"]));
        });

        return app;
    }
}

public sealed record OpenIdConfiguration(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("id_token_signing_alg_values_supported")] string[] SigningAlgorithms);