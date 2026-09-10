namespace FleetGo.API.RateLimiting;

/// <summary>Names of the ASP.NET Core rate limiter policies registered in <c>Program.cs</c>.</summary>
internal static class RateLimiterPolicies
{
    /// <summary>Applied to <see cref="FleetGo.Shared.ApiRoutes.AuthOtpRequest"/>.</summary>
    public const string OtpRequest = "otp-request";

    /// <summary>Applied to <see cref="FleetGo.Shared.ApiRoutes.AuthOtpVerify"/>.</summary>
    public const string OtpVerify = "otp-verify";
}
