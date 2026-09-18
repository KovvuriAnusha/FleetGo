namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Adds a customer. Customers are shared reference data, not owned by any one driver - any
/// authenticated driver can create or edit one.
/// </summary>
/// <param name="Name">Customer or business name.</param>
/// <param name="PhoneNumber">Contact phone number, if known.</param>
/// <param name="Email">Contact email address, if known.</param>
/// <param name="AddressLine1">Primary address line.</param>
/// <param name="AddressLine2">Secondary address line (unit, suite), if applicable.</param>
/// <param name="City">Address city.</param>
/// <param name="State">Address state/province, if applicable.</param>
/// <param name="PostalCode">Address postal/ZIP code.</param>
/// <param name="Country">Address country, if known.</param>
public sealed record CreateCustomerRequest(
    string Name,
    string? PhoneNumber,
    string? Email,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string? Country);
