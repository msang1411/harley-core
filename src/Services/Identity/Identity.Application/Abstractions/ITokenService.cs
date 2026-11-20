using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
}