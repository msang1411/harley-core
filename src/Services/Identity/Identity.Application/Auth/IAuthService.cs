using Identity.Application.Common;

namespace Identity.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request);
    Task<Result<AuthResponse>> RefreshAsync(RefreshRequest request);
    Task LogoutAsync(LogoutRequest request);
    Task<Result<UserDto>> GetCurrentUserAsync(Guid userId);
}