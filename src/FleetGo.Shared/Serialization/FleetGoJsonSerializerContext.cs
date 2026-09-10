using System.Text.Json.Serialization;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;

namespace FleetGo.Shared.Serialization;

/// <summary>
/// Source-generated System.Text.Json metadata for every contract that crosses
/// the wire.
/// <para>
/// This matters most on mobile: .NET MAUI release builds are trimmed (and can be
/// AOT compiled on iOS), which strips the reflection metadata that the default
/// JSON serialiser relies on. Generating the serialisation code at compile time
/// removes that risk entirely and is measurably faster to start up.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiInfoResponse))]
[JsonSerializable(typeof(HealthReportResponse))]
[JsonSerializable(typeof(HealthCheckEntryResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(LogoutRequest))]
[JsonSerializable(typeof(CurrentUserResponse))]
[JsonSerializable(typeof(RequestOtpRequest))]
[JsonSerializable(typeof(VerifyOtpRequest))]
public sealed partial class FleetGoJsonSerializerContext : JsonSerializerContext;

