using BusinessOS.Api.Billing;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Customers;
using BusinessOS.Customers;

namespace BusinessOS.Api.Tests;

public sealed class OrganisationProfileTests
{
    [Fact]
    public void Profile_Validation_Accepts_Matching_Gstin_State()
    {
        var request = ValidRequest("22ABCDE1234F1Z5", "22");
        OrganisationProfileValidator.Validate(request);
    }

    [Fact]
    public void Profile_Validation_Rejects_Gstin_State_Mismatch()
    {
        var request = ValidRequest("22ABCDE1234F1Z5", "27");
        var error = Assert.Throws<ArgumentException>(
            () => OrganisationProfileValidator.Validate(request));
        Assert.Contains("must match billing state code", error.Message);
    }

    [Fact]
    public void Profile_Response_Is_Complete_With_Legal_Contact_And_Address()
    {
        var organisation = CreateOrganisation("22ABCDE1234F1Z5", "22");
        var response = OrganisationProfileValidator.ToResponse(organisation);
        Assert.True(response.BillingProfileComplete);
        Assert.Null(response.BillingProfileIssue);
        Assert.Equal("22", response.BillingAddress!.StateCode);
        Assert.Equal("Owner", response.PrimaryContact!.Designation);
    }

    [Fact]
    public void Billing_Uses_Billing_Address_State_When_Gstin_Is_Not_Available()
    {
        var organisation = CreateOrganisation(null, "27");
        var source = new CommerceBillingSource(
            organisation.TenantId, organisation.Id, Guid.NewGuid(), Guid.NewGuid(),
            "AI_REPAIR", 118m, "INR", "pay_profile_test",
            new DateTimeOffset(2026, 9, 18, 6, 0, 0, TimeSpan.Zero), false);
        var draft = BillingCalculator.CreateDraft(
            source, organisation, new BillingOptions { SellerStateCode = "22" },
            new BillingInvoiceCreateRequest(), "FreeTesting");

        Assert.Equal("27", draft.Buyer.StateCode);
        Assert.Equal("InterState", draft.Tax.SupplyType);
        Assert.Equal(18m, draft.Tax.IgstAmount);
    }

    private static OrganisationProfileUpdateRequest ValidRequest(
        string? gstin,
        string stateCode) =>
        new(
            "Acme Repair",
            "Acme Repair Pvt. Ltd.",
            gstin,
            new OrganisationProfileContact(null, "Amit", "amit@example.test", "9999999999", "Owner"),
            new OrganisationBillingAddress(
                null, "Main Road", null, "Raipur", "Chhattisgarh",
                "492001", "IN", stateCode));

    private static Organisation CreateOrganisation(string? gstin, string stateCode)
    {
        var organisation = new Organisation(
            Guid.NewGuid(), Guid.NewGuid(), "Acme Repair", "Acme Repair Pvt. Ltd.", gstin);
        organisation.AddRole(OrganisationRole.Customer);
        organisation.AddContact(new ContactPerson(
            Guid.NewGuid(), "Amit", "amit@example.test", "9999999999", true, "Owner"));
        organisation.AddAddress(new OrganisationAddress(
            Guid.NewGuid(), "Main Road", null, "Raipur", "Chhattisgarh",
            "492001", "IN", true, stateCode));
        return organisation;
    }
}
