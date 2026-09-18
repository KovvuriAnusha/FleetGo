using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// Builders for the Phase 4A contracts the driver screens display. The contracts are
/// positional records with ten-odd members each; without these, every test would be mostly
/// constructor noise and a reader could not see which value the test actually cares about.
/// </summary>
internal static class FleetTestData
{
    public static RouteResponse Route(
        string routeNumber = "R-001",
        RouteStatus status = RouteStatus.Planned,
        int stopCount = 0,
        Guid? id = null,
        Guid? vehicleId = null,
        string? vehicleRegistrationNumber = null,
        DateOnly? routeDate = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            RouteNumber: routeNumber,
            DriverId: Guid.NewGuid(),
            DriverCode: "D-1001",
            VehicleId: vehicleId,
            VehicleRegistrationNumber: vehicleRegistrationNumber,
            RouteDate: routeDate ?? new DateOnly(2026, 9, 18),
            Status: status,
            StopCount: stopCount,
            CreatedAtUtc: DateTime.UnixEpoch,
            UpdatedAtUtc: DateTime.UnixEpoch);

    public static StopResponse Stop(
        int sequence = 1,
        string customerName = "Acme Ltd",
        StopStatus status = StopStatus.Pending,
        int packageCount = 0,
        Guid? id = null,
        Guid? routeId = null,
        Guid? customerId = null,
        string? deliveryNotes = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            RouteId: routeId ?? Guid.NewGuid(),
            CustomerId: customerId ?? Guid.NewGuid(),
            CustomerName: customerName,
            Sequence: sequence,
            Status: status,
            DeliveryNotes: deliveryNotes,
            PackageCount: packageCount,
            CreatedAtUtc: DateTime.UnixEpoch,
            UpdatedAtUtc: DateTime.UnixEpoch);

    public static PackageResponse Package(
        string trackingNumber = "TRK-001",
        PackageStatus status = PackageStatus.Pending,
        string? description = null,
        Guid? id = null,
        Guid? stopId = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            StopId: stopId ?? Guid.NewGuid(),
            TrackingNumber: trackingNumber,
            Description: description,
            Status: status,
            CreatedAtUtc: DateTime.UnixEpoch,
            UpdatedAtUtc: DateTime.UnixEpoch);

    public static VehicleResponse Vehicle(
        string registrationNumber = "VAN-01",
        string make = "Ford",
        string model = "Transit",
        Guid? id = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            RegistrationNumber: registrationNumber,
            Make: make,
            Model: model,
            Year: 2024,
            Status: VehicleStatus.Active,
            DriverId: null,
            DriverCode: null,
            CreatedAtUtc: DateTime.UnixEpoch,
            UpdatedAtUtc: DateTime.UnixEpoch);

    public static CustomerResponse Customer(
        string name = "Acme Ltd",
        string addressLine1 = "1 Test Street",
        string city = "Testville",
        string postalCode = "00000",
        Guid? id = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            Name: name,
            PhoneNumber: "+44 20 7946 0000",
            Email: "ops@acme.example",
            AddressLine1: addressLine1,
            AddressLine2: null,
            City: city,
            State: null,
            PostalCode: postalCode,
            Country: null,
            CreatedAtUtc: DateTime.UnixEpoch,
            UpdatedAtUtc: DateTime.UnixEpoch);

    /// <summary>One page containing exactly <paramref name="items"/>, as the API would return it.</summary>
    public static PagedResponse<T> Page<T>(params T[] items) => new(items, 1, 20, items.Length);

    /// <summary>A page that is part of a larger result set, for paging tests.</summary>
    public static PagedResponse<T> PageOf<T>(int page, int pageSize, int totalCount, params T[] items) =>
        new(items, page, pageSize, totalCount);
}
