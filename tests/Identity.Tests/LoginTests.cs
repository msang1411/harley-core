namespace Identity.Tests;

[Collection(ApiCollection.Name)]
public sealed class LoginTests(IdentityApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsTokens()
    {
        var (_, email) = await RegisterNewUserAsync();

        var response = await LoginAsync(email, TestData.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await ReadAuthAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var (_, email) = await RegisterNewUserAsync();

        var response = await LoginAsync(email, "WrongPassw0rd");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameErrorAsWrongPassword()
    {
        // The two failures must be indistinguishable, otherwise an attacker could
        // discover which email addresses are registered.
        var (_, email) = await RegisterNewUserAsync();

        var wrongPassword = await LoginAsync(email, "WrongPassw0rd");
        var unknownEmail = await LoginAsync(TestData.UniqueEmail(), TestData.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        var titleA = (await ReadJsonAsync(wrongPassword)).GetProperty("title").GetString();
        var titleB = (await ReadJsonAsync(unknownEmail)).GetProperty("title").GetString();
        Assert.Equal(titleA, titleB);
    }

    [Fact]
    public async Task Login_WithEmptyFields_ReturnsBadRequest()
    {
        var response = await LoginAsync("", "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_LocksTheAccount()
    {
        var (_, email) = await RegisterNewUserAsync();

        // ASP.NET Identity defaults: 5 failed attempts lock the account for 5 minutes.
        for (var i = 0; i < 5; i++)
        {
            var failed = await LoginAsync(email, "WrongPassw0rd");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        // Even the correct password is rejected while the account is locked.
        var response = await LoginAsync(email, TestData.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}