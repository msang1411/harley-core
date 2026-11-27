using System.Security.Claims;
using BuildingBlocks.Common.Authentication;
using Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Tests;

/// <summary>
/// Contract tests: a downstream service built with BuildingBlocks.Common must accept tokens
/// issued by Identity using only the published discovery document and JWKS.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JwksConsumerTests(IdentityApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Consumer_AcceptsTokenIssuedByIdentity_AndReadsUserId()
    {
        var (auth, _) = await RegisterNewUserAsync();
        var me = await ReadJsonAsync(await GetMeAsync(auth.AccessToken));
        var expectedUserId = Guid.Parse(me.GetProperty("id").GetString()!);

        await using var consumer = await StartConsumerAsync();
        var response = await Send(consumer, auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(expectedUserId, Guid.Parse(body.GetProperty("userId").GetString()!));
    }

    [Fact]
    public async Task Consumer_WithoutToken_ReturnsUnauthorized()
    {
        await using var consumer = await StartConsumerAsync();

        var response = await Send(consumer, accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Consumer_ConfiguredForAnotherAudience_RejectsToken()
    {
        var (auth, _) = await RegisterNewUserAsync();

        await using var consumer = await StartConsumerAsync(audience: "some-other-api");
        var response = await Send(consumer, auth.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Consumer_RejectsTokenWithTamperedSignature()
    {
        var (auth, _) = await RegisterNewUserAsync();
        var parts = auth.AccessToken.Split('.');
        parts[2] = new string('A', parts[2].Length);

        await using var consumer = await StartConsumerAsync();
        var response = await Send(consumer, string.Join('.', parts));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Send(WebApplication consumer, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }

        return await consumer.GetTestClient().SendAsync(request);
    }

    // Starts a tiny in-memory service that behaves like a future Blog service.
    private async Task<WebApplication> StartConsumerAsync(string? audience = null)
    {
        var jwt = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = jwt.Issuer,
            ["Jwt:Audience"] = audience ?? jwt.Audience,
            ["Jwt:MetadataAddress"] = "http://localhost/.well-known/openid-configuration",
            ["Jwt:RequireHttpsMetadata"] = "false"
        });

        builder.Services.AddJwtAuthentication(builder.Configuration);

        // Send the discovery and JWKS downloads to the in-memory Identity host instead of the network.
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            options.BackchannelHttpHandler = Factory.Server.CreateHandler());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/whoami", (ClaimsPrincipal user) => Results.Ok(new { userId = user.GetUserId() }))
            .RequireAuthorization();

        await app.StartAsync();
        return app;
    }
}