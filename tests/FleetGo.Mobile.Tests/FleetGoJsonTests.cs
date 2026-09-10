using System.Text.Json;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// Guards the serialisation settings both sides of the wire share. Release builds
/// of the mobile app are trimmed, so these contracts must round-trip through the
/// source-generated context rather than reflection.
/// </summary>
public sealed class FleetGoJsonTests
{
    [Fact]
    public void ApiInfoResponse_RoundTrips_ThroughTheSourceGeneratedContext()
    {
        ApiInfoResponse original = new(
            Name: "FleetGo API",
            Version: "0.1.0",
            Environment: "Development",
            ServerTimeUtc: new DateTimeOffset(2026, 9, 9, 8, 30, 0, TimeSpan.Zero));

        string json = JsonSerializer.Serialize(original, FleetGoJsonSerializerContext.Default.ApiInfoResponse);
        ApiInfoResponse? restored = JsonSerializer.Deserialize(json, FleetGoJsonSerializerContext.Default.ApiInfoResponse);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Contracts_AreSerialisedAsCamelCase()
    {
        ApiInfoResponse info = new("FleetGo API", "0.1.0", "Development", DateTimeOffset.UnixEpoch);

        string json = JsonSerializer.Serialize(info, FleetGoJsonSerializerContext.Default.ApiInfoResponse);

        Assert.Contains("\"serverTimeUtc\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedOptions_UseTheSourceGeneratedResolver()
    {
        Assert.Same(FleetGoJsonSerializerContext.Default, FleetGoJson.Options.TypeInfoResolver);
    }
}
