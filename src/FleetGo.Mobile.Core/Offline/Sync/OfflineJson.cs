using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>
/// Source-generated serialisation for what the outbox stores in its payload column.
/// <para>
/// Same reasoning as <c>FleetGo.Shared.Serialization.FleetGoJsonSerializerContext</c>: MAUI
/// release builds are trimmed and iOS is AOT compiled, so reflection-based serialisation is
/// a runtime failure waiting to happen. A separate context rather than an addition to the
/// shared one because these types never cross the wire - they are this device's private
/// record of what it still owes the server.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusChangePayload))]
[JsonSerializable(typeof(EntitySnapshot))]
public sealed partial class OfflineJsonSerializerContext : JsonSerializerContext;

/// <summary>Convenience wrappers so call sites do not repeat the context lookup.</summary>
public static class OfflineJson
{
    public static string Serialize(StatusChangePayload payload) =>
        JsonSerializer.Serialize(payload, OfflineJsonSerializerContext.Default.StatusChangePayload);

    /// <summary>
    /// Reads a payload back. Returns null for anything unreadable rather than throwing: a row
    /// written by an older build of the app should strand one operation for a person to look
    /// at, not crash the sync engine on every run from then on.
    /// </summary>
    public static StatusChangePayload? TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, OfflineJsonSerializerContext.Default.StatusChangePayload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
