using System.Security.Claims;
using System.Text;
using FleetGo.API.Auth.Options;
using FleetGo.API.Data.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FleetGo.API.Auth;

/// <inheritdoc cref="IJwtTokenService" />
internal sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    // JsonWebTokenHandler (Microsoft.IdentityModel.JsonWebTokens) rather than the older
    // JwtSecurityTokenHandler: it is the actively developed, higher-throughput token
    // handler and is what the ASP.NET Core JwtBearer handler itself uses to validate.
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public AccessToken CreateAccessToken(User user)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        DateTime expiresAtUtc = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        ];

        if (user.Driver is { } driver)
        {
            claims.Add(new Claim(FleetGoClaimTypes.DriverId, driver.Id.ToString()));
            claims.Add(new Claim(FleetGoClaimTypes.DriverCode, driver.DriverCode));
        }

        SigningCredentials signingCredentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256Signature);

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = expiresAtUtc,
            SigningCredentials = signingCredentials,
        };

        string token = _tokenHandler.CreateToken(descriptor);

        return new AccessToken(token, expiresAtUtc);
    }
}

