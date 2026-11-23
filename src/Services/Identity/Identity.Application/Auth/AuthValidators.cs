using FluentValidation;

namespace Identity.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        // Stop at the first failure so an empty email does not also report "invalid format".
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        // Password complexity (digits, casing, length) is enforced by ASP.NET Identity options.
        // Keeping it out of here gives the password policy a single source of truth.
        // The upper bound only protects the server from expensive hashing of huge inputs.
        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(x => x.FullName)
            .MaximumLength(100)
            .When(x => x.FullName is not null);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(256);

        // No password policy checks on login: a wrong password must look like any other
        // wrong password, otherwise we would leak the password rules to an attacker.
        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(128);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        // A real token is 43 characters (32 random bytes, base64url). 200 is a generous cap.
        RuleFor(x => x.RefreshToken)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(200);
    }
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        // Logout is idempotent and always returns 204, so an empty token is tolerated.
        // We only reject absurdly large payloads.
        RuleFor(x => x.RefreshToken)
            .MaximumLength(200);
    }
}