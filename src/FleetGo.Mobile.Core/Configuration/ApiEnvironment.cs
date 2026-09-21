namespace FleetGo.Mobile.Core.Configuration;

/// <summary>
/// Decides which API the app talks to, from the value baked in at build time and the
/// platform's development fallback.
/// <para>
/// The policy lives here, in a platform-free project, rather than in the MAUI head, so it
/// can be tested directly - the rule that a Release build must never quietly fall back to a
/// developer's loopback address is exactly the kind of thing that should have a test.
/// </para>
/// </summary>
public static class ApiEnvironment
{
    /// <summary>
    /// The MSBuild property, and the assembly-metadata key it is written to, that carries the
    /// API base address into the built app. See FleetGo.Mobile.csproj and
    /// docs/development-setup.md.
    /// </summary>
    public const string BaseAddressMetadataKey = "FleetGo.ApiBaseAddress";

    /// <summary>
    /// Resolves the API base address.
    /// </summary>
    /// <param name="configuredBaseAddress">
    /// The address supplied at build time (<c>-p:FleetGoApiBaseAddress=...</c>), or null when
    /// none was given.
    /// </param>
    /// <param name="developmentFallback">
    /// The loopback address this platform uses when running against a locally hosted API.
    /// Debug builds pass one; Release builds pass null, which is what makes a missing
    /// configuration an error instead of a silent localhost.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Neither an address nor a fallback was supplied, or the supplied address is not a
    /// usable absolute http/https URL. Failing here - loudly, at startup - is the point:
    /// an app that cannot say which backend it is for should not pretend it has one.
    /// </exception>
    public static Uri ResolveBaseAddress(string? configuredBaseAddress, string? developmentFallback)
    {
        string? candidate = !string.IsNullOrWhiteSpace(configuredBaseAddress)
            ? configuredBaseAddress
            : developmentFallback;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException(
                $"No API base address is configured. Build with -p:FleetGoApiBaseAddress=https://your-api.example " +
                "(see docs/development-setup.md, \"Pointing the app at an API\"). Debug builds fall back to the " +
                "local development API; Release builds deliberately do not.");
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? baseAddress)
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"The configured API base address '{candidate}' is not an absolute http or https URL.");
        }

        return baseAddress;
    }
}
