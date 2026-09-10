namespace FleetGo.API.Auth.Options;

/// <summary>
/// Bound from the <c>Jwt</c> configuration section. <see cref="SigningKey"/> is the one
/// field that must never appear in a committed appsettings file - see
/// docs/development-setup.md for the <c>dotnet user-secrets</c> command that sets it locally.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Issuer (<c>iss</c> claim) stamped into every access token and required on validation.</summary>
    public required string Issuer { get; set; }

    /// <summary>Audience (<c>aud</c> claim) stamped into every access token and required on validation.</summary>
    public required string Audience { get; set; }

    /// <summary>
    /// Symmetric signing key, HMAC-SHA256. Must be at least 32 bytes once UTF-8 encoded
    /// (256 bits) - <see cref="Program"/> fails fast at startup if it is shorter or missing,
    /// rather than silently signing tokens with a weak key.
    /// </summary>
    public required string SigningKey { get; set; }

    /// <summary>How long an access token is valid for. Kept short - refresh tokens exist so this can be.</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
}

