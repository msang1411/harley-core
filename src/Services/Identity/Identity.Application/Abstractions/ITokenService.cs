using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

// Token = original value (sent to the client; not stored in the database)
// Hash  = value stored in the database
public sealed record RefreshTokenValue(string Token, string Hash, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
    RefreshTokenValue CreateRefreshToken();
    string HashRefreshToken(string token);
}