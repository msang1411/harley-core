namespace Gateway.Api.Configuration;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    // Applies to login and register: the endpoints that accept credentials.
    public LimitRule Credentials { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    // Refresh tokens are high entropy, so this limit can be more generous.
    public LimitRule Refresh { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
}

public sealed class LimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}