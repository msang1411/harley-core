namespace Identity.Tests;

[Collection(ApiCollection.Name)]
public sealed class RefreshTokenTests(IdentityApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokenPair()
    {
        var (first, _) = await RegisterNewUserAsync();

        var response = await RefreshAsync(first.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await ReadAuthAsync(response);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(second.AccessToken));
    }

    [Fact]
    public async Task Refresh_NewAccessToken_IsAccepted()
    {
        var (first, _) = await RegisterNewUserAsync();
        var second = await ReadAuthAsync(await RefreshAsync(first.RefreshToken));

        var me = await GetMeAsync(second.AccessToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_ReturnsUnauthorized()
    {
        var response = await RefreshAsync("this-token-does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithEmptyToken_ReturnsBadRequest()
    {
        var response = await RefreshAsync("");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_ReturnsUnauthorized()
    {
        var (auth, _) = await RegisterNewUserAsync();
        await Factory.ExpireTokenAsync(auth.RefreshToken);

        var response = await RefreshAsync(auth.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingAnAlreadyUsedToken_RevokesTheWholeFamily()
    {
        var (first, _) = await RegisterNewUserAsync();

        // R1 is exchanged for R2 (rotation).
        var second = await ReadAuthAsync(await RefreshAsync(first.RefreshToken));

        // Presenting R1 again signals theft: the request fails...
        var reuse = await RefreshAsync(first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // ...and the legitimate R2 is revoked too, because it belongs to the same family.
        var afterReuse = await RefreshAsync(second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithConcurrentRequests_OnlyOneSucceeds()
    {
        var (auth, _) = await RegisterNewUserAsync();

        // Fire several refreshes with the same token at once. The atomic UPDATE in
        // TryMarkUsedAsync must let exactly one of them win.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5)
                .Select(_ => RefreshAsync(auth.RefreshToken))
                .ToArray());

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Login_CreatesAnIndependentFamily_SoOtherSessionsSurviveReuse()
    {
        var (sessionA, email) = await RegisterNewUserAsync();
        var sessionB = await ReadAuthAsync(await LoginAsync(email, TestData.Password));

        // Trigger reuse detection on session A only.
        await RefreshAsync(sessionA.RefreshToken);
        await RefreshAsync(sessionA.RefreshToken);

        // Session B (another device) must be unaffected.
        var response = await RefreshAsync(sessionB.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var (auth, _) = await RegisterNewUserAsync();

        var logout = await LogoutAsync(auth.RefreshToken);
        var refresh = await RefreshAsync(auth.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_WithUnknownToken_StillReturnsNoContent()
    {
        // Logout is idempotent and must not reveal whether a token exists.
        var response = await LogoutAsync("this-token-does-not-exist");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_IsStoredAsHashOnly()
    {
        var (auth, _) = await RegisterNewUserAsync();

        var row = await Factory.FindStoredTokenAsync(auth.RefreshToken);

        Assert.NotNull(row);
        Assert.NotEqual(auth.RefreshToken, row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length); // SHA-256 as hex
        Assert.False(await Factory.IsStoredAsPlainTextAsync(auth.RefreshToken));
    }
}