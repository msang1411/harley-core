using Gateway.Api.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services.AddGatewayCors(builder.Configuration);
builder.Services.AddGatewayRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseRouting();

// Order matters: CORS must answer preflight requests before the rate limiter sees them,
// otherwise preflights would consume the login permits.
app.UseCors();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { service = "gateway", status = "running" }));
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();