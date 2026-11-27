using System.Threading.RateLimiting;
using Gateway.Api.Configuration;
using Microsoft.AspNetCore.RateLimiting;

namespace Gateway.Api.Extensions;

public static class RateLimitPolicies
{
    // Referenced by name from the "RateLimiterPolicy" property of the YARP routes.
    public const string Credentials = "auth-credentials";
    public const string Refresh = "auth-refresh";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
                       ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Each policy owns its counters, so being throttled on login does not affect refresh.
            options.AddPolicy(RateLimitPolicies.Credentials, context => PerIpLimiter(context, settings.Credentials));
            options.AddPolicy(RateLimitPolicies.Refresh, context => PerIpLimiter(context, settings.Refresh));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;

                // Tell well-behaved clients when they may try again.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers["Retry-After"] = Math.Ceiling(retryAfter.TotalSeconds).ToString("0");
                }

                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting")
                    .LogWarning("Rate limit hit from {Ip} on {Path}", http.Connection.RemoteIpAddress, http.Request.Path);

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests. Please try again later.")
                    .ExecuteAsync(http);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> PerIpLimiter(HttpContext context, LimitRule rule)
    {
        // Behind a reverse proxy this is the proxy's address unless ForwardedHeaders is configured
        // with trusted proxies; see the Gateway section in CLAUDE.md.
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Sliding window avoids the "double burst" at the boundary of a fixed window.
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rule.PermitLimit,
            Window = TimeSpan.FromSeconds(rule.WindowSeconds),
            SegmentsPerWindow = 6,
            QueueLimit = 0 // Reject immediately instead of queueing requests.
        });
    }
}