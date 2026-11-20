using Identity.Application.Abstractions;
using Identity.Application.Common;
using Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Identity.Application.Auth;

public sealed class AuthService(UserManager<ApplicationUser> userManager, ITokenService tokenService)
    : IAuthService
{
    private static readonly Error InvalidCredentials =
        new(ErrorType.Unauthorized, "InvalidCredentials", "Incorrect email or password.");

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

        return Result<AuthResponse>.Success(await IssueTokenAsync(user));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Result<AuthResponse>.Failure(InvalidCredentials);

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Result<AuthResponse>.Failure(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
            return Result<AuthResponse>.Failure(InvalidCredentials);

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return Result<AuthResponse>.Failure(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return Result<AuthResponse>.Success(await IssueTokenAsync(user));
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result<UserDto>.Failure(new Error(ErrorType.NotFound, "UserNotFound", "User not found."));

        var roles = await userManager.GetRolesAsync(user);
        return Result<UserDto>.Success(new UserDto(user.Id, user.Email!, user.FullName, roles.ToList()));
    }

    private async Task<AuthResponse> IssueTokenAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.CreateAccessToken(user, roles);
        return new AuthResponse(token.Token, token.ExpiresAtUtc);
    }
}