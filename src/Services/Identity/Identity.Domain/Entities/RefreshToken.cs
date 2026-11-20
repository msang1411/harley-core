namespace Identity.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // All tokens generated from the same login session share the same FamilyId.
    public Guid FamilyId { get; set; }

    // SHA-256 (hex) of the original token. NEVER store the original token.
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    // Valid = token has been exchanged for a new token
    public DateTime? UsedAtUtc { get; set; }

    // Valid = revoked (due to logout or detection of reuse)
    public DateTime? RevokedAtUtc { get; set; }
}