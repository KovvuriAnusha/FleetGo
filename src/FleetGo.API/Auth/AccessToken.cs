namespace FleetGo.API.Auth;

/// <summary>A freshly minted access token together with its expiry.</summary>
public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

