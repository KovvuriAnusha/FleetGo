using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetGo.Shared.Serialization;

/// <summary>
/// The one set of JSON settings used by both the API and the mobile app, so a
/// payload always serialises and deserialises identically on both sides.
/// </summary>
public static class FleetGoJson
{
    /// <summary>
    /// Web defaults (camelCase, case-insensitive reads) backed by the
    /// source-generated <see cref="FleetGoJsonSerializerContext"/>.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = FleetGoJsonSerializerContext.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
