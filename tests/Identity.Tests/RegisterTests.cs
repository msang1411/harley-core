namespace Identity.Tests;

[Collection(ApiCollection.Name)]
public sealed class RegisterTests(IdentityApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Register_WithValidData_ReturnsCreatedWithTokens()
    {
        var response = await RegisterRawAsync(TestData.UniqueEmail());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await ReadAuthAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal("Bearer", auth.TokenType);
        Assert.True(auth.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var email = TestData.UniqueEmail();
        await RegisterRawAsync(email);

        var second = await RegisterRawAsync(email);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("Sh0rt")]          // shorter than the minimum length
    [InlineData("alllowercase1")]  // no uppercase letter
    [InlineData("NoDigitsHere")]   // no digit
    public async Task Register_WithWeakPassword_ReturnsBadRequest(string password)
    {
        // These pass the FluentValidation shape checks and are rejected by ASP.NET Identity,
        // which proves the two layers cooperate.
        var response = await RegisterRawAsync(TestData.UniqueEmail(), password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Register_WithInvalidEmail_ReturnsValidationProblem(string email)
    {
        var response = await RegisterRawAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("errors").TryGetProperty("Email", out _));
    }

    [Fact]
    public async Task Register_WithOversizedPassword_ReturnsValidationProblem()
    {
        var response = await RegisterRawAsync(TestData.UniqueEmail(), new string('a', 129));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("errors").TryGetProperty("Password", out _));
    }
}