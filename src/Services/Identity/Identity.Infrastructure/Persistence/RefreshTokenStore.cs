using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

public sealed class RefreshTokenStore(AppDbContext db) : IRefreshTokenStore
{
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryMarkUsedAsync(Guid tokenId, DateTime nowUtc, CancellationToken ct = default)
    {
        var rows = await db.RefreshTokens
            .Where(t => t.Id == tokenId && t.UsedAtUtc == null && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, nowUtc), ct);

        return rows == 1;
    }

    public async Task RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken ct = default)
    {
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, nowUtc), ct);
    }
}