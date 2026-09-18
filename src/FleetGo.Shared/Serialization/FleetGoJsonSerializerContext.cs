using System.Text.Json.Serialization;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Contracts.Fleet;

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
[JsonSerializable(typeof(VehicleResponse))]
[JsonSerializable(typeof(CreateVehicleRequest))]
[JsonSerializable(typeof(UpdateVehicleRequest))]
[JsonSerializable(typeof(PagedResponse<VehicleResponse>))]
[JsonSerializable(typeof(CustomerResponse))]
[JsonSerializable(typeof(CreateCustomerRequest))]
[JsonSerializable(typeof(UpdateCustomerRequest))]
[JsonSerializable(typeof(PagedResponse<CustomerResponse>))]
[JsonSerializable(typeof(RouteResponse))]
[JsonSerializable(typeof(CreateRouteRequest))]
[JsonSerializable(typeof(UpdateRouteRequest))]
[JsonSerializable(typeof(PagedResponse<RouteResponse>))]
[JsonSerializable(typeof(StopResponse))]
[JsonSerializable(typeof(CreateStopRequest))]
[JsonSerializable(typeof(UpdateStopRequest))]
[JsonSerializable(typeof(PagedResponse<StopResponse>))]
[JsonSerializable(typeof(PackageResponse))]
[JsonSerializable(typeof(CreatePackageRequest))]
[JsonSerializable(typeof(UpdatePackageRequest))]
[JsonSerializable(typeof(PagedResponse<PackageResponse>))]
public sealed partial class FleetGoJsonSerializerContext : JsonSerializerContext;

