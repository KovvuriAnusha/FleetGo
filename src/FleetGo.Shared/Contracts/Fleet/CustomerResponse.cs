namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>Delivery recipient, as returned by the customer endpoints.</summary>
/// <param name="Id">Stable identifier for the customer.</param>
/// <param name="Name">Customer or business name.</param>
/// <param name="PhoneNumber">Contact phone number, when on file.</param>
/// <param name="Email">Contact email address, when on file.</param>
/// <param name="AddressLine1">Primary address line.</param>
/// <param name="AddressLine2">Secondary address line (unit, suite), when applicable.</param>
/// <param name="City">Address city.</param>
/// <param name="State">Address state/province, when applicable.</param>
/// <param name="PostalCode">Address postal/ZIP code.</param>
/// <param name="Country">Address country, when recorded.</param>
/// <param name="CreatedAtUtc">When the customer record was created.</param>
/// <param name="UpdatedAtUtc">When the customer record was last changed.</param>
public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string? PhoneNumber,
    string? Email,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string? Country,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
