namespace Identity.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string? FullName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string TokenType = "Bearer");

public sealed record UserDto(Guid Id, string Email, string? FullName, IReadOnlyList<string> Roles);