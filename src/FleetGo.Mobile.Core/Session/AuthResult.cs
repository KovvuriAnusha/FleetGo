namespace FleetGo.Mobile.Core.Session;

/// <summary>
/// Outcome of a login attempt. A plain result rather than an exception: "wrong password"
/// is an everyday, expected outcome of logging in, not a failure of the login mechanism
/// itself - <see cref="LoginViewModel"/> checks <see cref="Succeeded"/> instead of writing
/// a try/catch around every attempt.
/// </summary>
public sealed record AuthResult(bool Succeeded, string? ErrorMessage)
{
    public static AuthResult Success { get; } = new(true, null);

    public static AuthResult Failure(string errorMessage) => new(false, errorMessage);
}

