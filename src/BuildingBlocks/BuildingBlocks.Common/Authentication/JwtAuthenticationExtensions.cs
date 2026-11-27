using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Common.Authentication;

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication that validates tokens issued by Identity using the public key
    /// published through the OpenID Connect discovery document and JWKS endpoint.
    /// Remember to call UseAuthentication() and UseAuthorization() in the pipeline.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail fast at startup when the configuration is incomplete,
        // instead of failing on the first request.
        services.AddOptions<JwtValidationOptions>()
            .Bind(configuration.GetSection(JwtValidationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtValidationOptions>>((bearer, settings) =>
            {
                var jwt = settings.Value;

                // The handler downloads the signing keys from here, caches them, and refreshes
                // them when it meets an unknown "kid" (key rotation).
                bearer.MetadataAddress = jwt.MetadataAddress;
                bearer.RequireHttpsMetadata = jwt.RequireHttpsMetadata;

                // Keep claim names as issued ("sub", "role") instead of remapping them to long URIs.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,

                    // Accept RS256 only. This blocks algorithm-confusion attacks
                    // (for example a token "signed" with HS256 using the public key as the secret).
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
                    NameClaimType = JwtClaimTypes.Subject,
                    RoleClaimType = JwtClaimTypes.Role
                };
            });

        services.AddAuthorization();
        return services;
    }
}