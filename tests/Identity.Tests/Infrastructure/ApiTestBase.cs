namespace Identity.Tests.Infrastructure;

/// <summary>
/// Base class with small helpers so each test reads as a short scenario.
/// Derived classes must be annotated with [Collection(ApiCollection.Name)].
/// </summary>
public abstract class ApiTestBase(IdentityApiFactory factory)
{
    protected IdentityApiFactory Factory { get; } = factory;

    // Each test class gets its own client; all of them talk to the same in-process API.
    protected HttpClient Client { get; } = factory.CreateClient();

    protected Task<HttpResponseMessage> RegisterRawAsync(string email, string password = TestData.Password) =>
        Client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, password, "Test User"));

    protected Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password));

    protected Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(refreshToken));

    protected Task<HttpResponseMessage> LogoutAsync(string refreshToken) =>
        Client.PostAsJsonAsync("/auth/logout", new LogoutRequest(refreshToken));

    protected async Task<HttpResponseMessage> GetMeAsync(string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }

        return await Client.SendAsync(request);
    }

    /// <summary>Registers a brand-new user and returns the tokens plus the credentials used.</summary>
    protected async Task<(AuthResponse Auth, string Email)> RegisterNewUserAsync()
    {
        var email = TestData.UniqueEmail();
        var response = await RegisterRawAsync(email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAuthAsync(response), email);
    }

    protected static async Task<AuthResponse> ReadAuthAsync(HttpResponseMessage response)
    {
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
    }

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}