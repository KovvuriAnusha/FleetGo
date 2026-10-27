using System.Globalization;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Core.Offline;

/// <summary>
/// Translates between the API's wire contracts and the flattened records the local database
/// holds.
/// <para>
/// This mapping exists on purpose rather than storing the contracts directly. The cache is
/// this device's own store, and giving it its own shape means a wire contract can gain a
/// field without a schema migration, and the schema can carry columns the wire has no reason
/// to know about - <c>SyncedAtUtc</c>, <c>HasPendingLocalChange</c>. It is one small file, and
/// it is the reason the rest of Phase 5 never has to think about the difference.
/// </para>
/// </summary>
public static class FleetRecordMapper
{
    private const string DateFormat = "yyyy-MM-dd";

    // --- server -> local --------------------------------------------------------------

    public static LocalRoute ToLocal(RouteResponse route, DateTime syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(route);

        return new LocalRoute
        {
            Id = route.Id,
            RouteNumber = route.RouteNumber,
            DriverId = route.DriverId,
            DriverCode = route.DriverCode,
            VehicleId = route.VehicleId,
            VehicleRegistrationNumber = route.VehicleRegistrationNumber,
            RouteDate = route.RouteDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            Status = route.Status.ToString(),
            StopCount = route.StopCount,
            UpdatedAtUtc = route.UpdatedAtUtc,
            SyncedAtUtc = syncedAtUtc,
        };
    }

    public static LocalStop ToLocal(StopResponse stop, DateTime syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(stop);

        return new LocalStop
        {
            Id = stop.Id,
            RouteId = stop.RouteId,
            CustomerId = stop.CustomerId,
            CustomerName = stop.CustomerName,
            Sequence = stop.Sequence,
            Status = stop.Status.ToString(),
            DeliveryNotes = stop.DeliveryNotes,
            PackageCount = stop.PackageCount,
            UpdatedAtUtc = stop.UpdatedAtUtc,
            SyncedAtUtc = syncedAtUtc,
        };
    }

    public static LocalPackage ToLocal(PackageResponse package, DateTime syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new LocalPackage
        {
            Id = package.Id,
            StopId = package.StopId,
            TrackingNumber = package.TrackingNumber,
            Description = package.Description,
            Status = package.Status.ToString(),
            UpdatedAtUtc = package.UpdatedAtUtc,
            SyncedAtUtc = syncedAtUtc,
        };
    }

    public static LocalVehicle ToLocal(VehicleResponse vehicle, DateTime syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        return new LocalVehicle
        {
            Id = vehicle.Id,
            RegistrationNumber = vehicle.RegistrationNumber,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Year = vehicle.Year,
            Status = vehicle.Status.ToString(),
            SyncedAtUtc = syncedAtUtc,
        };
    }

    public static LocalCustomer ToLocal(CustomerResponse customer, DateTime syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new LocalCustomer
        {
            Id = customer.Id,
            Name = customer.Name,
            PhoneNumber = customer.PhoneNumber,
            Email = customer.Email,
            AddressLine1 = customer.AddressLine1,
            AddressLine2 = customer.AddressLine2,
            City = customer.City,
            State = customer.State,
            PostalCode = customer.PostalCode,
            Country = customer.Country,
            SyncedAtUtc = syncedAtUtc,
        };
    }

    // --- local -> the shape the screens already bind to --------------------------------
    //
    // The view models keep speaking the wire contracts, so every Phase 4B page, DataTemplate
    // and test carries on working whether the data came off the network or out of SQLite.
    // Swapping them for local record types would have meant touching every x:DataType in the
    // app to gain nothing a driver can see.
    //
    // CreatedAtUtc is the one field the cache does not keep: no screen shows it, so storing
    // it would be a column that exists only to be mapped back. It comes out as default, and
    // that is deliberate rather than an oversight.

    public static RouteResponse ToContract(LocalRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return new RouteResponse(
            route.Id,
            route.RouteNumber,
            route.DriverId,
            route.DriverCode,
            route.VehicleId,
            route.VehicleRegistrationNumber,
            ParseDate(route.RouteDate),
            ParseStatus(route.Status, RouteStatus.Planned),
            route.StopCount,
            CreatedAtUtc: default,
            route.UpdatedAtUtc);
    }

    public static StopResponse ToContract(LocalStop stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        return new StopResponse(
            stop.Id,
            stop.RouteId,
            stop.CustomerId,
            stop.CustomerName,
            stop.Sequence,
            ParseStatus(stop.Status, StopStatus.Pending),
            stop.DeliveryNotes,
            stop.PackageCount,
            CreatedAtUtc: default,
            stop.UpdatedAtUtc);
    }

    public static PackageResponse ToContract(LocalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new PackageResponse(
            package.Id,
            package.StopId,
            package.TrackingNumber,
            package.Description,
            ParseStatus(package.Status, PackageStatus.Pending),
            CreatedAtUtc: default,
            package.UpdatedAtUtc);
    }

    public static VehicleResponse ToContract(LocalVehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        return new VehicleResponse(
            vehicle.Id,
            vehicle.RegistrationNumber,
            vehicle.Make,
            vehicle.Model,
            vehicle.Year,
            ParseStatus(vehicle.Status, VehicleStatus.Active),
            DriverId: null,
            DriverCode: null,
            CreatedAtUtc: default,
            UpdatedAtUtc: default);
    }

    public static CustomerResponse ToContract(LocalCustomer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new CustomerResponse(
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            customer.Email,
            customer.AddressLine1,
            customer.AddressLine2,
            customer.City,
            customer.State,
            customer.PostalCode,
            customer.Country,
            CreatedAtUtc: default,
            UpdatedAtUtc: default);
    }

    /// <summary>The ISO form the local store uses for dates, so callers never hand-roll the format string.</summary>
    public static string ToStorageDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
            ? parsed
            : default;

    /// <summary>
    /// Statuses are stored as names, so a value this build does not recognise can only come
    /// from a database written by a newer one. Falling back to the conservative "not started
    /// yet" member keeps the screen readable instead of throwing, and the next sync overwrites
    /// it with whatever the server actually says.
    /// </summary>
    private static TStatus ParseStatus<TStatus>(string value, TStatus fallback)
        where TStatus : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out TStatus parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
}
