using System.Reflection;
using FleetGo.Mobile.Core.Configuration;

namespace FleetGo.Mobile.Configuration;

/// <summary>
/// Where the app expects to find the FleetGo API, and how patient it is with it.
/// <para>
/// The address is supplied at build time through the <c>FleetGoApiBaseAddress</c> MSBuild
/// property, which the project file writes into assembly metadata (see FleetGo.Mobile.csproj).
/// A Debug build with no property falls back to the local development API, so nothing about
/// the day-to-day loop changes. A Release build has no fallback: if no address was supplied,
/// <see cref="ApiEnvironment.ResolveBaseAddress"/> throws rather than shipping an app quietly
/// pointed at a developer's laptop. The project file also fails the Release build outright for
/// the same reason, so that error is a backstop rather than the first line of defence.
/// </para>
/// </summary>
public sealed record ApiSettings(Uri BaseAddress, TimeSpan Timeout)
{
    /// <summary>Matches the "http" profile in FleetGo.API/Properties/launchSettings.json.</summary>
    private const int LocalApiPort = 5266;

    /// <summary>
    /// Builds the settings the app runs with. See the type's own remarks for how the address
    /// is chosen, and docs/development-setup.md for how to supply one.
    /// </summary>
    public static ApiSettings Create() =>
        new(
            ApiEnvironment.ResolveBaseAddress(ReadConfiguredBaseAddress(), ResolveDevelopmentFallback()),
            TimeSpan.FromSeconds(30));

    /// <summary>The value baked in by the build, or null when none was supplied.</summary>
    private static string? ReadConfiguredBaseAddress() =>
        typeof(ApiSettings).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == ApiEnvironment.BaseAddressMetadataKey)
            ?.Value;

    /// <summary>
    /// The loopback address that means "the machine running the API" for this platform -
    /// Debug builds only. Each platform reaches the developer's machine differently, which is
    /// one of the classic first-day mobile debugging traps.
    /// </summary>
    private static string? ResolveDevelopmentFallback()
    {
#if DEBUG
#if ANDROID
        // The Android emulator sits behind its own NAT, where 10.0.2.2 is an alias for the
        // host machine's loopback. On a physical device, build with -p:FleetGoApiBaseAddress
        // set to the development machine's LAN address instead.
        return $"http://10.0.2.2:{LocalApiPort}";
#else
        // iOS Simulator and Mac Catalyst share the host's network stack.
        return $"http://localhost:{LocalApiPort}";
#endif
#else
        // Release: no fallback on purpose.
        return null;
#endif
    }
}
