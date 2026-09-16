namespace BusinessOS.Customers;

public enum OrganisationStatus
{
    Active = 1,
    Inactive = 2,
    Archived = 3
}

public enum OrganisationRole
{
    Customer = 1,
    Partner = 2,
    Supplier = 3
}

public sealed record ContactPerson(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    bool IsPrimary,
    string? Designation = null);

public sealed record OrganisationAddress(
    Guid Id,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    bool IsPrimary);