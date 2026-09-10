namespace FleetGo.Shared.Contracts;

/// <summary>
/// Metadata about the running API instance. Used by the mobile app to confirm
/// which backend and which build it is talking to.
/// </summary>
/// <param name="Name">Friendly service name, for example "FleetGo API".</param>
/// <param name="Version">Informational assembly version of the deployed build.</param>
/// <param name="Environment">Hosting environment name (Development, Staging, Production).</param>
/// <param name="ServerTimeUtc">Current server time in UTC, useful for clock-skew checks.</param>
public sealed record ApiInfoResponse(
    string Name,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc);
