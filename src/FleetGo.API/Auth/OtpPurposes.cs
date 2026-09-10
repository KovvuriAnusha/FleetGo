namespace FleetGo.API.Auth;

/// <summary>
/// The set of things an OTP can be issued for. A plain string column (see
/// <see cref="FleetGo.API.Data.Entities.OtpCode.Purpose"/>) rather than a numeric enum: it
/// reads directly in the database without a lookup table, and a later phase can introduce a
/// new purpose (e.g. <c>PasswordReset</c>) without a migration touching existing rows.
/// "New OTP invalidates the previous one" and "max attempts" are scoped per user *and*
/// purpose, so a login OTP in flight is never disturbed by, say, an unrelated
/// phone-number-change OTP for the same user in a later phase.
/// </summary>
internal static class OtpPurposes
{
    /// <summary>Signing in - the only purpose Phase 3 issues.</summary>
    public const string Login = "login";
}
