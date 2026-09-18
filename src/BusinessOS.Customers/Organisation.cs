namespace BusinessOS.Customers;

public sealed class Organisation
{
    private readonly HashSet<OrganisationRole> _roles = [];
    private readonly List<ContactPerson> _contacts = [];
    private readonly List<OrganisationAddress> _addresses = [];

    public Organisation(
        Guid id,
        Guid tenantId,
        string name,
        string? legalName = null,
        string? gstin = null,
        string? displayCode = null,
        OrganisationStatus status = OrganisationStatus.Active)
    {
        if (id == Guid.Empty) throw new ArgumentException("Organisation id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Organisation name is required.", nameof(name));

        Id = id;
        TenantId = tenantId;
        Name = name.Trim();
        LegalName = Clean(legalName);
        Gstin = Clean(gstin)?.ToUpperInvariant();
        DisplayCode = Clean(displayCode);
        Status = status;
    }
    public Guid Id { get; }
    public Guid TenantId { get; }
    public string Name { get; private set; }
    public string? LegalName { get; private set; }
    public string? Gstin { get; private set; }
    public string? DisplayCode { get; private set; }
    public OrganisationStatus Status { get; private set; }
    public IReadOnlyCollection<OrganisationRole> Roles => _roles;
    public IReadOnlyList<ContactPerson> Contacts => _contacts.AsReadOnly();
    public IReadOnlyList<OrganisationAddress> Addresses => _addresses.AsReadOnly();
    public ContactPerson? PrimaryContact => _contacts.SingleOrDefault(x => x.IsPrimary);
    public OrganisationAddress? PrimaryAddress => _addresses.SingleOrDefault(x => x.IsPrimary);

    public bool HasRole(OrganisationRole role) => _roles.Contains(role);

    public void AddRole(OrganisationRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        _roles.Add(role);
    }

    public void RemoveRole(OrganisationRole role) => _roles.Remove(role);

    public void SetStatus(OrganisationStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        Status = status;
    }
    public void AddContact(ContactPerson contact)
    {
        if (contact.Id == Guid.Empty) throw new ArgumentException("Contact id is required.", nameof(contact));
        if (string.IsNullOrWhiteSpace(contact.Name)) throw new ArgumentException("Contact name is required.", nameof(contact));
        if (_contacts.Any(x => x.Id == contact.Id))
            throw new InvalidOperationException("Contact id already exists.");

        if (contact.IsPrimary)
        {
            for (var i = 0; i < _contacts.Count; i++)
                _contacts[i] = _contacts[i] with { IsPrimary = false };
        }

        _contacts.Add(contact with
        {
            Name = contact.Name.Trim(),
            Email = Clean(contact.Email),
            Phone = Clean(contact.Phone),
            Designation = Clean(contact.Designation)
        });
    }

    public void UpdateContact(Guid contactId, string name, string? email, string? phone, bool isPrimary, string? designation = null)
    {
        if (contactId == Guid.Empty) throw new ArgumentException("Contact id is required.", nameof(contactId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Contact name is required.", nameof(name));
        var index = _contacts.FindIndex(x => x.Id == contactId);
        if (index < 0) throw new InvalidOperationException("Contact does not exist.");
        if (isPrimary)
        {
            for (var i = 0; i < _contacts.Count; i++)
                _contacts[i] = _contacts[i] with { IsPrimary = false };
        }
        _contacts[index] = _contacts[index] with
        {
            Name = name.Trim(),
            Email = Clean(email),
            Phone = Clean(phone),
            IsPrimary = isPrimary,
            Designation = Clean(designation)
        };
    }

    public void RemoveContact(Guid contactId)
    {
        if (contactId == Guid.Empty) throw new ArgumentException("Contact id is required.", nameof(contactId));
        var index = _contacts.FindIndex(x => x.Id == contactId);
        if (index < 0) throw new InvalidOperationException("Contact does not exist.");
        var wasPrimary = _contacts[index].IsPrimary;
        _contacts.RemoveAt(index);
        if (wasPrimary && _contacts.Count > 0 && _contacts.All(x => !x.IsPrimary))
            _contacts[0] = _contacts[0] with { IsPrimary = true };
    }

    public void AddAddress(OrganisationAddress address)
    {
        if (address.Id == Guid.Empty) throw new ArgumentException("Address id is required.", nameof(address));
        ValidateAddress(address.Line1, address.City, address.State, address.PostalCode, address.CountryCode);
        if (_addresses.Any(x => x.Id == address.Id))
            throw new InvalidOperationException("Address id already exists.");

        if (address.IsPrimary)
        {
            for (var i = 0; i < _addresses.Count; i++)
                _addresses[i] = _addresses[i] with { IsPrimary = false };
        }

        _addresses.Add(NormalizeAddress(address));
    }

    public void UpdateAddress(
        Guid addressId,
        string line1,
        string? line2,
        string city,
        string state,
        string postalCode,
        string countryCode,
        bool isPrimary,
        string? stateCode = null)
    {
        if (addressId == Guid.Empty) throw new ArgumentException("Address id is required.", nameof(addressId));
        ValidateAddress(line1, city, state, postalCode, countryCode);
        var index = _addresses.FindIndex(x => x.Id == addressId);
        if (index < 0) throw new InvalidOperationException("Address does not exist.");
        if (isPrimary)
        {
            for (var i = 0; i < _addresses.Count; i++)
                _addresses[i] = _addresses[i] with { IsPrimary = false };
        }
        _addresses[index] = NormalizeAddress(new OrganisationAddress(
            addressId, line1, line2, city, state, postalCode, countryCode, isPrimary, stateCode));
    }

    public void RemoveAddress(Guid addressId)
    {
        if (addressId == Guid.Empty) throw new ArgumentException("Address id is required.", nameof(addressId));
        var index = _addresses.FindIndex(x => x.Id == addressId);
        if (index < 0) throw new InvalidOperationException("Address does not exist.");
        var wasPrimary = _addresses[index].IsPrimary;
        _addresses.RemoveAt(index);
        if (wasPrimary && _addresses.Count > 0 && _addresses.All(x => !x.IsPrimary))
            _addresses[0] = _addresses[0] with { IsPrimary = true };
    }

    public void UpdateProfile(string name, string? legalName, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organisation name is required.", nameof(name));

        Name = name.Trim();
        LegalName = Clean(legalName);
        Gstin = Clean(gstin)?.ToUpperInvariant();
    }

    public void SetDisplayCode(string? displayCode) => DisplayCode = Clean(displayCode);

    private static void ValidateAddress(string line1, string city, string state, string postalCode, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(line1)) throw new ArgumentException("Address line is required.", nameof(line1));
        if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("City is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("State is required.", nameof(state));
        if (string.IsNullOrWhiteSpace(postalCode)) throw new ArgumentException("Postal code is required.", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(countryCode)) throw new ArgumentException("Country code is required.", nameof(countryCode));
    }

    private static OrganisationAddress NormalizeAddress(OrganisationAddress address) => address with
    {
        Line1 = address.Line1.Trim(),
        Line2 = Clean(address.Line2),
        City = address.City.Trim(),
        State = address.State.Trim(),
        PostalCode = address.PostalCode.Trim(),
        CountryCode = address.CountryCode.Trim().ToUpperInvariant(),
        StateCode = Clean(address.StateCode)
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
