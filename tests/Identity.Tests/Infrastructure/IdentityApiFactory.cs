using System.Security.Cryptography;
using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Identity.Tests.Infrastructure;

/// <summary>
/// Boots the real Identity API in-process against a throwaway PostgreSQL container.
/// A single instance is shared by all tests (see ApiCollection), so the container
/// is started only once per test run.
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string[] OverriddenVariables =
    [
        "ConnectionStrings__Default",
        "Jwt__PrivateKeyPath",
        "Database__AutoMigrate"
    ];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    private string? _keyPath;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Generate a throwaway RSA key so tests never touch the real keys/ folder.
        _keyPath = Path.Combine(Path.GetTempPath(), $"harley-test-{Guid.NewGuid():N}.pem");
        using (var rsa = RSA.Create(2048))
        {
            await File.WriteAllTextAsync(_keyPath, rsa.ExportPkcs8PrivateKeyPem());
        }

        // Environment variables are used on purpose: Program.cs reads the connection string
        // eagerly, before the test host can override configuration. Environment variables are
        // already visible at that point and take precedence over appsettings*.json.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", _keyPath);

        // The app applies EF migrations itself on startup, exactly like in Docker.
        Environment.SetEnvironmentVariable("Database__AutoMigrate", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" skips appsettings.Development.json and user-secrets (local dev values).
        builder.UseEnvironment("Testing");
    }

    /// <summary>Loads the stored refresh token row for a raw token, or null if none exists.</summary>
    public async Task<RefreshToken?> FindStoredTokenAsync(string rawToken)
    {
        using var scope = Services.CreateScope();
        var hash = scope.ServiceProvider.GetRequiredService<ITokenService>().HashRefreshToken(rawToken);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash);
    }

    /// <summary>True if the raw token value itself (not its hash) is present in the database.</summary>
    public async Task<bool> IsStoredAsPlainTextAsync(string rawToken)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RefreshTokens.AnyAsync(t => t.TokenHash == rawToken);
    }

    /// <summary>Forces a refresh token to be expired, simulating the passage of time.</summary>
    public async Task ExpireTokenAsync(string rawToken)
    {
        using var scope = Services.CreateScope();
        var hash = scope.ServiceProvider.GetRequiredService<ITokenService>().HashRefreshToken(rawToken);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    // xUnit's IAsyncLifetime.DisposeAsync returns Task, while WebApplicationFactory.DisposeAsync
    // returns ValueTask, so the xUnit one must be implemented explicitly.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();

        foreach (var variable in OverriddenVariables)
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        if (_keyPath is not null && File.Exists(_keyPath))
        {
            File.Delete(_keyPath);
        }
    }
}