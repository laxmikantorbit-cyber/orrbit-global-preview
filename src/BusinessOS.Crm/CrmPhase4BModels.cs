namespace BusinessOS.Crm;

public enum CrmEstimateRequestStatus
{
    New = 1,
    Reviewing = 2,
    Converted = 3,
    Closed = 4
}

public sealed class CrmEstimateRequest
{
    public CrmEstimateRequest(
        Guid id, Guid tenantId, string source, string requirement,
        string? contactName = null, string? mobileNumber = null, string? email = null,
        decimal? expectedValue = null, Guid? assignedUserId = null,
        DateTimeOffset? createdAtUtc = null, string? businessCompany = null, string? notes = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Estimate request id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        Id = id;
        TenantId = tenantId;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Status = CrmEstimateRequestStatus.New;
        Apply(source, requirement, contactName, mobileNumber, email, expectedValue, assignedUserId, businessCompany, notes, false);
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public string Source { get; private set; } = string.Empty;
    public string Requirement { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? MobileNumber { get; private set; }
    public string? Email { get; private set; }
    public decimal? ExpectedValue { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public string? BusinessCompany { get; private set; }
    public string? Notes { get; private set; }
    public CrmEstimateRequestStatus Status { get; private set; }
    public Guid? ConvertedLeadId { get; private set; }
    public Guid? ConvertedEstimateId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string source, string requirement, string? contactName, string? mobileNumber,
        string? email, decimal? expectedValue, Guid? assignedUserId, string? businessCompany = null, string? notes = null)
    {
        if (Status is CrmEstimateRequestStatus.Converted or CrmEstimateRequestStatus.Closed)
            throw new InvalidOperationException("Converted or closed estimate requests cannot be edited.");
        Apply(source, requirement, contactName, mobileNumber, email, expectedValue, assignedUserId, businessCompany, notes, true);
    }

    public void AssignTo(Guid? assignedUserId)
    {
        if (Status is CrmEstimateRequestStatus.Converted or CrmEstimateRequestStatus.Closed)
            throw new InvalidOperationException("Converted or closed estimate requests cannot be reassigned.");
        AssignedUserId = assignedUserId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ChangeStatus(CrmEstimateRequestStatus status)
    {
        if (status == Status) return;
        if (status == CrmEstimateRequestStatus.Reviewing) { StartReview(); return; }
        if (status == CrmEstimateRequestStatus.Closed) { Close(); return; }
        throw new InvalidOperationException("Estimate request status can only move to Reviewing or Closed directly.");
    }

    public void StartReview()
    {
        if (Status != CrmEstimateRequestStatus.New)
            throw new InvalidOperationException("Only new estimate requests can move to review.");
        Status = CrmEstimateRequestStatus.Reviewing;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkConverted(Guid leadId, Guid? estimateId = null)
    {
        if (leadId == Guid.Empty) throw new ArgumentException("Converted lead id is required.", nameof(leadId));
        EnsureConvertible();
        ConvertedLeadId = leadId;
        ConvertedEstimateId = estimateId;
        CompleteConversion();
    }

    public void MarkConvertedToEstimate(Guid estimateId)
    {
        if (estimateId == Guid.Empty) throw new ArgumentException("Converted estimate id is required.", nameof(estimateId));
        EnsureConvertible();
        ConvertedLeadId = null;
        ConvertedEstimateId = estimateId;
        CompleteConversion();
    }

    public void Close()
    {
        if (Status == CrmEstimateRequestStatus.Converted)
            throw new InvalidOperationException("Converted estimate requests cannot be closed.");
        Status = CrmEstimateRequestStatus.Closed;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static CrmEstimateRequest Restore(
        Guid id, Guid tenantId, string source, string requirement,
        string? contactName, string? mobileNumber, string? email, decimal? expectedValue,
        Guid? assignedUserId, CrmEstimateRequestStatus status,
        Guid? convertedLeadId, Guid? convertedEstimateId,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc,
        string? businessCompany = null, string? notes = null)
    {
        var request = new CrmEstimateRequest(id, tenantId, source, requirement, contactName, mobileNumber,
            email, expectedValue, assignedUserId, createdAtUtc, businessCompany, notes);
        request.Status = status;
        request.ConvertedLeadId = convertedLeadId;
        request.ConvertedEstimateId = convertedEstimateId;
        request.UpdatedAtUtc = updatedAtUtc;
        return request;
    }

    private void EnsureConvertible()
    {
        if (Status == CrmEstimateRequestStatus.Converted)
            throw new InvalidOperationException("Estimate request is already converted.");
        if (Status == CrmEstimateRequestStatus.Closed)
            throw new InvalidOperationException("Closed estimate requests cannot be converted.");
    }

    private void CompleteConversion()
    {
        Status = CrmEstimateRequestStatus.Converted;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private void Apply(string source, string requirement, string? contactName, string? mobileNumber,
        string? email, decimal? expectedValue, Guid? assignedUserId, string? businessCompany, string? notes, bool touch)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(requirement)) throw new ArgumentException("Requirement is required.", nameof(requirement));
        if (expectedValue.HasValue && expectedValue.Value < 0m) throw new ArgumentOutOfRangeException(nameof(expectedValue));
        Source = source.Trim();
        Requirement = requirement.Trim();
        ContactName = Clean(contactName);
        MobileNumber = Clean(mobileNumber);
        Email = Clean(email)?.ToLowerInvariant();
        ExpectedValue = expectedValue.HasValue ? decimal.Round(expectedValue.Value, 2, MidpointRounding.AwayFromZero) : null;
        AssignedUserId = assignedUserId;
        BusinessCompany = Clean(businessCompany);
        Notes = Clean(notes);
        if (touch) UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum CrmKnowledgeArticleStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3
}

public enum CrmKnowledgeVisibility
{
    Team = 1,
    Private = 2
}

public sealed class CrmKnowledgeCategory
{
    public CrmKnowledgeCategory(Guid id, Guid tenantId, string name, int sortOrder = 0, bool active = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Category id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Category name is required.", nameof(name));
        Id = id; TenantId = tenantId; Name = name.Trim(); SortOrder = sortOrder; Active = active;
    }
    public Guid Id { get; }
    public Guid TenantId { get; }
    public string Name { get; private set; }
    public int SortOrder { get; private set; }
    public bool Active { get; private set; }
    public void Update(string name, int sortOrder, bool active)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Category name is required.", nameof(name));
        Name = name.Trim(); SortOrder = sortOrder; Active = active;
    }
}

public sealed class CrmKnowledgeArticle
{
    public CrmKnowledgeArticle(
        Guid id, Guid tenantId, string title, string content, Guid categoryId,
        Guid ownerUserId, CrmKnowledgeVisibility visibility = CrmKnowledgeVisibility.Team,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Article id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (categoryId == Guid.Empty) throw new ArgumentException("Category id is required.", nameof(categoryId));
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        Id = id; TenantId = tenantId; CategoryId = categoryId; OwnerUserId = ownerUserId;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow; UpdatedAtUtc = CreatedAtUtc;
        Status = CrmKnowledgeArticleStatus.Draft;
        Apply(title, content, categoryId, ownerUserId, visibility, false);
    }
    public Guid Id { get; }
    public Guid TenantId { get; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public CrmKnowledgeVisibility Visibility { get; private set; }
    public CrmKnowledgeArticleStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string title, string content, Guid categoryId, Guid ownerUserId, CrmKnowledgeVisibility visibility)
    {
        if (Status == CrmKnowledgeArticleStatus.Archived)
            throw new InvalidOperationException("Archived articles cannot be edited.");
        Apply(title, content, categoryId, ownerUserId, visibility, true);
    }
    public void Publish()
    {
        if (Status != CrmKnowledgeArticleStatus.Draft)
            throw new InvalidOperationException("Only draft articles can be published.");
        Status = CrmKnowledgeArticleStatus.Published; UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
    public void Archive()
    {
        if (Status == CrmKnowledgeArticleStatus.Archived)
            throw new InvalidOperationException("Article is already archived.");
        Status = CrmKnowledgeArticleStatus.Archived; UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
    public static CrmKnowledgeArticle Restore(
        Guid id, Guid tenantId, string title, string content, Guid categoryId, Guid ownerUserId,
        CrmKnowledgeVisibility visibility, CrmKnowledgeArticleStatus status,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        var article = new CrmKnowledgeArticle(id, tenantId, title, content, categoryId, ownerUserId, visibility, createdAtUtc);
        article.Status = status; article.UpdatedAtUtc = updatedAtUtc; return article;
    }
    private void Apply(string title, string content, Guid categoryId, Guid ownerUserId, CrmKnowledgeVisibility visibility, bool touch)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Article title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Article content is required.", nameof(content));
        if (categoryId == Guid.Empty) throw new ArgumentException("Category id is required.", nameof(categoryId));
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        if (!Enum.IsDefined(visibility)) throw new ArgumentOutOfRangeException(nameof(visibility));
        Title = title.Trim(); Content = content.Trim(); CategoryId = categoryId; OwnerUserId = ownerUserId; Visibility = visibility;
        if (touch) UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

public sealed class CrmMediaAsset
{
    public CrmMediaAsset(
        Guid id, Guid tenantId, string fileName, string mimeType, long sizeBytes,
        string purpose, string storageReference, Guid uploadedByUserId,
        string? entityType = null, Guid? entityId = null, DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Media id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (uploadedByUserId == Guid.Empty) throw new ArgumentException("Uploader user id is required.", nameof(uploadedByUserId));
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(mimeType)) throw new ArgumentException("MIME type is required.", nameof(mimeType));
        if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Purpose is required.", nameof(purpose));
        if (string.IsNullOrWhiteSpace(storageReference)) throw new ArgumentException("Storage reference is required.", nameof(storageReference));
        if (entityId.HasValue && string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Entity type is required when entity id is supplied.", nameof(entityType));
        Id = id; TenantId = tenantId; FileName = fileName.Trim(); MimeType = mimeType.Trim().ToLowerInvariant();
        SizeBytes = sizeBytes; Purpose = purpose.Trim(); StorageReference = storageReference.Trim();
        UploadedByUserId = uploadedByUserId; EntityType = Clean(entityType); EntityId = entityId;
        Active = true; CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow; UpdatedAtUtc = CreatedAtUtc;
    }
    public Guid Id { get; }
    public Guid TenantId { get; }
    public string FileName { get; private set; }
    public string MimeType { get; private set; }
    public long SizeBytes { get; private set; }
    public string Purpose { get; private set; }
    public string StorageReference { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }
    public bool Active { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public void SetActive(bool active) { Active = active; UpdatedAtUtc = DateTimeOffset.UtcNow; }
    public static CrmMediaAsset Restore(
        Guid id, Guid tenantId, string fileName, string mimeType, long sizeBytes, string purpose,
        string storageReference, Guid uploadedByUserId, string? entityType, Guid? entityId, bool active,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        var asset = new CrmMediaAsset(id, tenantId, fileName, mimeType, sizeBytes, purpose, storageReference,
            uploadedByUserId, entityType, entityId, createdAtUtc);
        asset.Active = active; asset.UpdatedAtUtc = updatedAtUtc; return asset;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
