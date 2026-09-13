namespace BusinessOS.Api.Customers;

public sealed record Customer(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Email);
