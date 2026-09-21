using FleetGo.Mobile.Core.Configuration;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// The rule these protect: a Release build must never quietly fall back to a developer's
/// loopback address. That is a configuration decision rather than a line of UI, so it is
/// tested here rather than discovered on a device.
/// </summary>
public sealed class ApiEnvironmentTests
{
    private const string DevelopmentFallback = "http://10.0.2.2:5266";

    [Fact]
    public void UsesTheAddressSuppliedAtBuildTime()
    {
        Uri baseAddress = ApiEnvironment.ResolveBaseAddress("https://api.example.test", DevelopmentFallback);

        Assert.Equal(new Uri("https://api.example.test"), baseAddress);
    }

    [Fact]
    public void FallsBackToTheDevelopmentAddress_WhenNothingWasConfigured()
    {
        // Debug builds pass a fallback, so the everyday loop keeps working with no extra flags.
        Uri baseAddress = ApiEnvironment.ResolveBaseAddress(configuredBaseAddress: null, DevelopmentFallback);

        Assert.Equal(new Uri(DevelopmentFallback), baseAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Throws_WhenNeitherAnAddressNorAFallbackIsAvailable(string? configuredBaseAddress)
    {
        // This is the Release path: no fallback is passed, so an unconfigured build fails
        // loudly instead of shipping an app pointed at a laptop.
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ApiEnvironment.ResolveBaseAddress(configuredBaseAddress, developmentFallback: null));

        Assert.Contains("FleetGoApiBaseAddress", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/api/v1")]
    [InlineData("ftp://files.example.test")]
    public void Throws_WhenTheConfiguredAddressIsNotAnAbsoluteHttpUrl(string configuredBaseAddress)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ApiEnvironment.ResolveBaseAddress(configuredBaseAddress, developmentFallback: null));

        Assert.Contains(configuredBaseAddress, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsPlainHttp_SoALocalOrLanDevelopmentApiStillWorks()
    {
        Uri baseAddress = ApiEnvironment.ResolveBaseAddress("http://192.168.1.10:5266", developmentFallback: null);

        Assert.Equal("http", baseAddress.Scheme);
    }
}
