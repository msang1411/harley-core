using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public interface IRefreshTokenStore
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    // Mark as "used" ATOMICALLY. 
    // Returns true if this call was the first to mark it as used; returns false if it had already been used or revoked by someone else.
    Task<bool> TryMarkUsedAsync(Guid tokenId, DateTime nowUtc, CancellationToken ct = default);

    // Revoke all valid tokens of a family
    Task RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken ct = default);
}