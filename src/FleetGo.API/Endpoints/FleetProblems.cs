using Microsoft.AspNetCore.Http.HttpResults;

namespace FleetGo.API.Endpoints;

/// <summary>
/// Shared <c>problem+json</c> factories for the fleet endpoints (Vehicle/Customer/Route/Stop/
/// Package), covering the response shapes that aren't already built in to
/// <see cref="TypedResults"/>. Mirrors the private helper pattern already used by
/// <see cref="AuthEndpoints"/>, just shared across more than one file.
/// </summary>
internal static class FleetProblems
{
    /// <summary>
    /// The caller is authenticated but not allowed to perform this action - e.g. assigning a
    /// resource to a driver other than themselves. Deliberately distinct from
    /// <see cref="TypedResults.NotFound()"/>, which this project uses instead whenever
    /// returning 403 would confirm that another driver's resource exists.
    /// </summary>
    public static ProblemHttpResult Forbidden(string detail) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Not authorized");

    /// <summary>The request is well-formed but conflicts with existing data (e.g. a duplicate identifier).</summary>
    public static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict");
}
