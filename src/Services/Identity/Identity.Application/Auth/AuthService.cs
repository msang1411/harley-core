using Identity.Application.Abstractions;
using Identity.Application.Common;
using Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Identity.Application.Auth;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IRefreshTokenStore refreshTokens,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly Error AccountTemporarilyLocked =
        new(
            ErrorType.Unauthorized,
            "AccountTemporarilyLocked",
            "Your account has been temporarily locked due to too many failed login attempts. Please try again in 5 minutes."
        );
        
    private static readonly Error InvalidCredentials =
        new(ErrorType.Unauthorized, "InvalidCredentials", "Incorrect email or password.");

    private static readonly Error InvalidRefreshToken =
        new(ErrorType.Unauthorized, "InvalidRefreshToken", "Refresh token is invalid or has expired.");

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Result<AuthResponse>.Failure(
                new Error(ErrorType.Validation, "Credentials", "Email and password are required."));

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = request.FullName?.Trim()
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            var errors = created.Errors.Select(e => new Error(
                e.Code.StartsWith("Duplicate") ? ErrorType.Conflict : ErrorType.Validation,
                e.Code,
                e.Description));
            return Result<AuthResponse>.Failure(errors);
        }

        // Once registered, you are automatically logged in: time to start a new family.
        return Result<AuthResponse>.Success(await IssueTokensAsync(user, Guid.NewGuid()));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Result<AuthResponse>.Failure(InvalidCredentials);

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Result<AuthResponse>.Failure(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
            return Result<AuthResponse>.Failure(AccountTemporarilyLocked);

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return Result<AuthResponse>.Failure(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        // Each login = a new family (each device/session has its own family)
        return Result<AuthResponse>.Success(await IssueTokensAsync(user, Guid.NewGuid()));
    }

    public async Task<Result<AuthResponse>> RefreshAsync(RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Result<AuthResponse>.Failure(InvalidRefreshToken);

        var now = DateTime.UtcNow;
        var stored = await refreshTokens.FindByHashAsync(tokenService.HashRefreshToken(request.RefreshToken));

        if (stored is null)
            return Result<AuthResponse>.Failure(InvalidRefreshToken);

        // Revoked (logged out or the family has been locked)
        if (stored.RevokedAtUtc is not null)
            return Result<AuthResponse>.Failure(InvalidRefreshToken);

        // Already used, yet brought out to be used again — suspected to have been stolen.
        if (stored.UsedAtUtc is not null)
        {
            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}, family {FamilyId}. Revoking family.",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now);
            return Result<AuthResponse>.Failure(InvalidRefreshToken);
        }

        // exprired
        if (stored.ExpiresAtUtc <= now)
            return Result<AuthResponse>.Failure(InvalidRefreshToken);

        // Acquire the right to use the token atomically. 
        // If two concurrent requests reach this point, only one will succeed.
        if (!await refreshTokens.TryMarkUsedAsync(stored.Id, now))
        {
            logger.LogWarning(
                "Concurrent use of refresh token for user {UserId}, family {FamilyId}. Revoking family.",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now);
            return Result<AuthResponse>.Failure(InvalidRefreshToken);
        }

        // Is the user still valid? (they may have been deleted or locked since they last logged in)
        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now);
            return Result<AuthResponse>.Failure(InvalidRefreshToken);
        }

        // Issue new tokens, keep the family the same
        return Result<AuthResponse>.Success(await IssueTokensAsync(user, stored.FamilyId));
    }

    public async Task LogoutAsync(LogoutRequest request)
    {
        // Logout is always "successful," regardless of whether the token is incorrect or non-existent (idempotent),
        // to avoid leaking information to someone attempting to probe for tokens.
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return;

        var stored = await refreshTokens.FindByHashAsync(tokenService.HashRefreshToken(request.RefreshToken));
        if (stored is null)
            return;

        await refreshTokens.RevokeFamilyAsync(stored.FamilyId, DateTime.UtcNow);
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result<UserDto>.Failure(new Error(ErrorType.NotFound, "UserNotFound", "User not found."));

        var roles = await userManager.GetRolesAsync(user);
        return Result<UserDto>.Success(new UserDto(user.Id, user.Email!, user.FullName, roles.ToList()));
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, Guid familyId)
    {
        var roles = await userManager.GetRolesAsync(user);
        var access = tokenService.CreateAccessToken(user, roles);
        var refresh = tokenService.CreateRefreshToken();

        await refreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = refresh.Hash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refresh.ExpiresAtUtc
        });

        return new AuthResponse(access.Token, access.ExpiresAtUtc, refresh.Token, refresh.ExpiresAtUtc);
    }
}