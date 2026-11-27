using Identity.Infrastructure.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Identity.Tests;

[Collection(ApiCollection.Name)]
public sealed class WellKnownTests(IdentityApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task OpenIdConfiguration_AdvertisesIssuerAndJwksUri()
    {
        var response = await Client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);

        var jwt = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        Assert.Equal(jwt.Issuer, json.GetProperty("issuer").GetString());

        var jwksUri = json.GetProperty("jwks_uri").GetString();
        Assert.NotNull(jwksUri);
        Assert.StartsWith("http", jwksUri);
        Assert.EndsWith("/.well-known/jwks.json", jwksUri);
    }
}