using Identity.Infrastructure.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Tests;

[Collection(ApiCollection.Name)]
public sealed class JwksTests(IdentityApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Jwks_ExposesPublicKeyMaterialOnly()
    {
        var response = await Client.GetAsync("/.well-known/jwks.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = (await ReadJsonAsync(response)).GetProperty("keys")[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.True(key.TryGetProperty("n", out _));
        Assert.True(key.TryGetProperty("e", out _));
        Assert.True(key.TryGetProperty("kid", out _));

        // These JWK members would reveal the private key and must never be published.
        foreach (var privateMember in new[] { "d", "p", "q", "dp", "dq", "qi" })
        {
            Assert.False(key.TryGetProperty(privateMember, out _), $"JWKS leaked private member '{privateMember}'.");
        }
    }

    [Fact]
    public async Task AccessToken_CanBeVerifiedUsingOnlyThePublishedJwks()
    {
        // This is exactly what another microservice will do: download the public key
        // and validate tokens without ever talking to Identity again.
        var (auth, _) = await RegisterNewUserAsync();
        var jwks = await ReadJsonAsync(await Client.GetAsync("/.well-known/jwks.json"));
        var publicKey = new JsonWebKey(jwks.GetProperty("keys")[0].GetRawText());
        var jwt = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            auth.AccessToken,
            new TokenValidationParameters
            {
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = publicKey
            });

        Assert.True(result.IsValid, result.Exception?.Message);
    }
}