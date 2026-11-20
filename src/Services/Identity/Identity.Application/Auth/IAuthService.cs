using Identity.Application.Common;

namespace Identity.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request);
    Task<Result<UserDto>> GetCurrentUserAsync(Guid userId);
}