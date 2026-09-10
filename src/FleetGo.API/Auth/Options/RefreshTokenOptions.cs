namespace FleetGo.API.Auth.Options;

/// <summary>Bound from the <c>RefreshToken</c> configuration section.</summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    /// <summary>How long a refresh token is valid for before it must be replaced by a fresh login.</summary>
    public int LifetimeDays { get; set; } = 30;
}

