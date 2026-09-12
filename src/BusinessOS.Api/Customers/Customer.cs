namespace BusinessOS.Api.Customers;

public sealed record Customer(
    Guid Id,
    string TenantId,
    string Name,
    string Email);
