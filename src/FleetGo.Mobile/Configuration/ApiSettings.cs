namespace FleetGo.Mobile.Configuration;

/// <summary>
/// Where the app expects to find the FleetGo API, and how patient it is with it.
/// <para>
/// Kept as an injected object rather than a static lookup so a later phase can
/// load it from configuration (or a build-time environment switch) without
/// touching the code that consumes it.
/// </para>
/// </summary>
public sealed record ApiSettings(Uri BaseAddress, TimeSpan Timeout)
{
    /// <summary>Matches the "http" profile in FleetGo.API/Properties/launchSettings.json.</summary>
    private const int LocalApiPort = 5266;

    /// <summary>
    /// Development defaults. Each platform reaches "the machine running the API"
    /// by a different address, which is one of the classic first-day mobile
    /// debugging traps.
    /// </summary>
    public static ApiSettings CreateDevelopmentDefaults() =>
        new(new Uri(ResolveDevelopmentHost()), TimeSpan.FromSeconds(30));

    private static string ResolveDevelopmentHost()
    {
#if ANDROID
        // The Android emulator sits behind its own NAT, where 10.0.2.2 is an alias
        // for the host machine's loopback. On a physical device, replace this with
        // the development machine's LAN IP (and make sure both are on one network).
        return $"http://10.0.2.2:{LocalApiPort}";
#else
        // iOS Simulator and Mac Catalyst share the host's network stack, so plain
        // localhost resolves to the machine running `dotnet run`.
        return $"http://localhost:{LocalApiPort}";
#endif
    }
}
