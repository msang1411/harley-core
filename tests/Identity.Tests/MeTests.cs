namespace Identity.Tests;

[Collection(ApiCollection.Name)]
public sealed class MeTests(IdentityApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await GetMeAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        var (auth, email) = await RegisterNewUserAsync();

        var response = await GetMeAsync(auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
    }

    [Fact]
    public async Task Me_WithTamperedSignature_ReturnsUnauthorized()
    {
        var (auth, _) = await RegisterNewUserAsync();

        // Replace the whole signature segment; changing a single character is not reliable
        // because the last base64url character may only carry padding bits.
        var parts = auth.AccessToken.Split('.');
        parts[2] = new string('A', parts[2].Length);
        var tampered = string.Join('.', parts);

        var response = await GetMeAsync(tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}