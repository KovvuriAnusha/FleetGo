namespace FleetGo.Mobile.Core.Session;

/// <summary>
/// A token pair as kept in secure on-device storage. Deliberately not the same type as
/// <c>FleetGo.Shared.Contracts.Auth.TokenResponse</c> - that record is the wire shape the
/// API returns; this one is what the device persists between launches, and the two are
/// free to diverge (they already do not need JSON source-generation metadata, since this
/// type never crosses the wire).
/// </summary>
public sealed record StoredTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

