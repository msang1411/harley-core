using System.Security.Claims;

namespace BuildingBlocks.Common.Authentication;

public static class JwtClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Role = "role";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns the user id stored in the "sub" claim, or null when it is missing or not a Guid.
    /// Services store only this id; they never keep a copy of the user's profile.
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(JwtClaimTypes.Subject)?.Value, out var id) ? id : null;
}