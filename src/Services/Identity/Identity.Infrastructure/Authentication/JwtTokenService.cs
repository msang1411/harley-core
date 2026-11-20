using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Authentication;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public SecurityKey SigningKey { get; }

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(_options.PrivateKeyPath));

        var keyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16];
        SigningKey = new RsaSecurityKey(rsa) { KeyId = keyId };
        _credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);
    }

    public AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (!string.IsNullOrWhiteSpace(user.FullName))
            claims.Add(new Claim("name", user.FullName));
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            SigningCredentials = _credentials
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }

    public RefreshTokenValue CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var expires = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);
        return new RefreshTokenValue(token, HashRefreshToken(token), expires);
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}