using System.Text.RegularExpressions;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Tenancy;
using BusinessOS.Customers;

namespace BusinessOS.Api.Customers;

public sealed record OrganisationProfileContact(
    Guid? Id,
    string Name,
    string? Email,
    string? Phone,
    string? Designation);

public sealed record OrganisationBillingAddress(
    Guid? Id,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    string StateCode);

public sealed record OrganisationProfileUpdateRequest(
    string Name,
    string? LegalName,
    string? Gstin,
    OrganisationProfileContact PrimaryContact,
    OrganisationBillingAddress BillingAddress);
public sealed record OrganisationProfileResponse(
    Guid OrganisationId,
    string Name,
    string? LegalName,
    string? Gstin,
    OrganisationProfileContact? PrimaryContact,
    OrganisationBillingAddress? BillingAddress,
    bool BillingProfileComplete,
    string? BillingProfileIssue);

public static class OrganisationProfileEndpoints
{
    public static IEndpointRouteBuilder MapOrganisationProfileEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(
            "/api/customer-portal/subscriptions/{subscriptionId:guid}/organisation-profile");
        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid subscriptionId,
        TenantContext tenant,
        ICommerceActivationStore commerce,
        IOrganisationRepository organisations,
        CancellationToken cancellationToken)
    {
        var state = await commerce.FindSubscriptionStateAsync(
            tenant.TenantId, subscriptionId, cancellationToken);
        if (state is null)
            return Results.NotFound(new ErrorResponse(
                "Subscription was not found for this tenant."));

        var organisation = await organisations.GetAsync(
            tenant.TenantId, state.OrganisationId, cancellationToken);
        return organisation is null
            ? Results.NotFound(new ErrorResponse("Customer organisation was not found."))
            : Results.Ok(OrganisationProfileValidator.ToResponse(organisation));
    }

    private static async Task<IResult> UpdateAsync(
        Guid subscriptionId,
        OrganisationProfileUpdateRequest request,
        TenantContext tenant,
        ICommerceActivationStore commerce,
        IOrganisationRepository organisations,
        CancellationToken cancellationToken)
    {
        if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
            return forbidden;

        try
        {
            OrganisationProfileValidator.Validate(request);
            var state = await commerce.FindSubscriptionStateAsync(
                tenant.TenantId, subscriptionId, cancellationToken);
            if (state is null)
                return Results.NotFound(new ErrorResponse(
                    "Subscription was not found for this tenant."));

            var organisation = await organisations.GetAsync(
                tenant.TenantId, state.OrganisationId, cancellationToken);
            if (organisation is null)
                return Results.NotFound(new ErrorResponse(
                    "Customer organisation was not found."));

            organisation.UpdateProfile(
                request.Name, request.LegalName, request.Gstin);
            OrganisationProfileValidator.UpsertPrimaryContact(
                organisation, request.PrimaryContact);
            OrganisationProfileValidator.UpsertBillingAddress(
                organisation, request.BillingAddress);

            await organisations.UpdateAsync(organisation, cancellationToken);
            return Results.Ok(OrganisationProfileValidator.ToResponse(organisation));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }
}

public static partial class OrganisationProfileValidator
{
    private static readonly Regex GstinPattern = new(
        "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static void Validate(OrganisationProfileUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Organisation name is required.");

        var contact = request.PrimaryContact
            ?? throw new ArgumentException("Primary contact is required.");
        if (string.IsNullOrWhiteSpace(contact.Name))
            throw new ArgumentException("Primary contact name is required.");

        var address = request.BillingAddress
            ?? throw new ArgumentException("Billing address is required.");
        if (string.IsNullOrWhiteSpace(address.Line1) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.State) ||
            string.IsNullOrWhiteSpace(address.PostalCode) ||
            string.IsNullOrWhiteSpace(address.CountryCode))
            throw new ArgumentException("Complete billing address is required.");

        var countryCode = address.CountryCode.Trim().ToUpperInvariant();
        if (countryCode.Length != 2)
            throw new ArgumentException("Country code must contain two letters.");

        var stateCode = address.StateCode?.Trim();
        if (stateCode is null || stateCode.Length != 2 || !stateCode.All(char.IsDigit))
            throw new ArgumentException(
                "Billing state code must be a two-digit GST state code.");

        var gstin = Clean(request.Gstin)?.ToUpperInvariant();
        if (gstin is not null && !GstinPattern.IsMatch(gstin))
            throw new ArgumentException("GSTIN format is invalid.");
        if (gstin is not null &&
            !gstin.StartsWith(stateCode, StringComparison.Ordinal))
            throw new ArgumentException(
                "GSTIN state code must match billing state code.");
    }

    public static void UpsertPrimaryContact(
        Organisation organisation,
        OrganisationProfileContact request)
    {
        var existing = organisation.PrimaryContact;
        if (existing is null)
        {
            organisation.AddContact(new ContactPerson(
                request.Id ?? Guid.NewGuid(),
                request.Name,
                request.Email,
                request.Phone,
                true,
                request.Designation));
            return;
        }

        organisation.UpdateContact(
            existing.Id,
            request.Name,
            request.Email,
            request.Phone,
            true,
            request.Designation);
    }
    public static void UpsertBillingAddress(
        Organisation organisation,
        OrganisationBillingAddress request)
    {
        var existing = organisation.PrimaryAddress;
        if (existing is null)
        {
            organisation.AddAddress(new OrganisationAddress(
                request.Id ?? Guid.NewGuid(),
                request.Line1,
                request.Line2,
                request.City,
                request.State,
                request.PostalCode,
                request.CountryCode,
                true,
                request.StateCode));
            return;
        }

        organisation.UpdateAddress(
            existing.Id,
            request.Line1,
            request.Line2,
            request.City,
            request.State,
            request.PostalCode,
            request.CountryCode,
            true,
            request.StateCode);
    }
    public static OrganisationProfileResponse ToResponse(Organisation organisation)
    {
        var contact = organisation.PrimaryContact;
        var address = organisation.PrimaryAddress;
        var complete = IsComplete(organisation, contact, address, out var issue);
        return new OrganisationProfileResponse(
            organisation.Id,
            organisation.Name,
            organisation.LegalName,
            organisation.Gstin,
            contact is null ? null : new OrganisationProfileContact(
                contact.Id, contact.Name, contact.Email,
                contact.Phone, contact.Designation),
            address is null ? null : new OrganisationBillingAddress(
                address.Id,
                address.Line1,
                address.Line2,
                address.City,
                address.State,
                address.PostalCode,
                address.CountryCode,
                address.StateCode ?? string.Empty),
            complete,
            issue);
    }
    private static bool IsComplete(
        Organisation organisation,
        ContactPerson? contact,
        OrganisationAddress? address,
        out string? issue)
    {
        issue = null;
        if (string.IsNullOrWhiteSpace(organisation.LegalName))
            issue = "Legal name is required for billing.";
        else if (contact is null || string.IsNullOrWhiteSpace(contact.Name))
            issue = "Primary contact is required.";
        else if (address is null ||
                 string.IsNullOrWhiteSpace(address.Line1) ||
                 string.IsNullOrWhiteSpace(address.City) ||
                 string.IsNullOrWhiteSpace(address.State) ||
                 string.IsNullOrWhiteSpace(address.PostalCode) ||
                 string.IsNullOrWhiteSpace(address.CountryCode))
            issue = "Complete billing address is required.";
        else if (string.IsNullOrWhiteSpace(address.StateCode))
            issue = "GST state code is required.";
        else if (!string.IsNullOrWhiteSpace(organisation.Gstin) &&
                 !organisation.Gstin.StartsWith(
                     address.StateCode, StringComparison.Ordinal))
            issue = "GSTIN state code does not match billing state code.";

        return issue is null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
