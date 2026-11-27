namespace Gateway.Api.Extensions;

public static class CorsPolicies
{
    // Referenced by name from the "CorsPolicy" property of the YARP routes.
    public const string Frontend = "frontend";
}

public static class CorsExtensions
{
    public static IServiceCollection AddGatewayCors(this IServiceCollection services, IConfiguration configuration)
    {
        // Exact origins only (scheme + host + port, no trailing slash).
        // An empty list means no browser origin is allowed.
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicies.Frontend, policy => policy
                .WithOrigins(origins)
                .WithMethods("GET", "POST", "OPTIONS")
                .WithHeaders("Authorization", "Content-Type")
                // Lets the browser cache the preflight answer, so it does not repeat it for every call.
                .SetPreflightMaxAge(TimeSpan.FromHours(1)));
        });

        return services;
    }
}