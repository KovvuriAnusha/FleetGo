namespace FleetGo.API.Data.Entities;

/// <summary>
/// A delivery recipient. Not owned by any one driver - the same customer can appear on
/// routes for different drivers over time - so, unlike Vehicle/Route, there is no driver
/// ownership to enforce here (see CustomerEndpoints for the resulting authorization model:
/// shared reference data, readable and writable by any authenticated driver).
/// </summary>
public sealed class Customer
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public required string AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public required string City { get; set; }

    public string? State { get; set; }

    public required string PostalCode { get; set; }

    public string? Country { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
