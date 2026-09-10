namespace FleetGo.Shared.Http;

/// <summary>
/// Well-known <see cref="HttpRequestOptions"/> keys used to pass per-request instructions
/// through the <see cref="HttpClient"/> pipeline without adding parameters to every method.
/// </summary>
public static class FleetGoHttpRequestOptions
{
    /// <summary>
    /// Set on a request to stop <see cref="BearerTokenHandler"/> from attaching an
    /// Authorization header to it. Used for the login and refresh calls themselves,
    /// which must never race with - or trigger - a token refresh of their own.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> SkipAuthentication = new("FleetGo.SkipAuthentication");
}

