namespace Identity.Tests;

public sealed class RequestValidatorTests
{
    private readonly RegisterRequestValidator _register = new();
    private readonly LoginRequestValidator _login = new();
    private readonly RefreshRequestValidator _refresh = new();
    private readonly LogoutRequestValidator _logout = new();

    [Fact]
    public void Register_WithValidInput_Passes()
    {
        var result = _register.Validate(new RegisterRequest("a@example.com", "Passw0rd123", "Alice"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "Passw0rd123")]
    [InlineData("not-an-email", "Passw0rd123")]
    [InlineData("a@example.com", "")]
    public void Register_WithInvalidInput_Fails(string email, string password)
    {
        var result = _register.Validate(new RegisterRequest(email, password, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Register_WithPasswordOver128Characters_Fails()
    {
        var result = _register.Validate(new RegisterRequest("a@example.com", new string('a', 129), null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Register_DoesNotEnforcePasswordComplexity()
    {
        // Complexity belongs to ASP.NET Identity (single source of truth), not to the validator.
        var result = _register.Validate(new RegisterRequest("a@example.com", "alllowercase", null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("a@example.com", "")]
    public void Login_WithMissingFields_Fails(string email, string password)
    {
        Assert.False(_login.Validate(new LoginRequest(email, password)).IsValid);
    }

    [Fact]
    public void Refresh_WithEmptyToken_Fails()
    {
        Assert.False(_refresh.Validate(new RefreshRequest("")).IsValid);
    }

    [Fact]
    public void Logout_WithEmptyToken_Passes()
    {
        // Logout is idempotent, so an empty token is tolerated.
        Assert.True(_logout.Validate(new LogoutRequest("")).IsValid);
    }
}