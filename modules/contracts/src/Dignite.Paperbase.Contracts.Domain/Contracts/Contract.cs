using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Contracts.Contracts;

public class Contract : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    public virtual Guid DocumentId { get; private set; }

    public virtual string DocumentTypeCode { get; private set; } = default!;

    public virtual string? Title { get; private set; }

    public virtual string? ContractNumber { get; private set; }

    public virtual string? PartyAName { get; private set; }

    public virtual string? PartyBName { get; private set; }

    public virtual string? CounterpartyName { get; private set; }

    public virtual DateTime? SignedDate { get; private set; }

    public virtual DateTime? EffectiveDate { get; private set; }

    public virtual DateTime? ExpirationDate { get; private set; }

    public virtual decimal? TotalAmount { get; private set; }

    public virtual string? Currency { get; private set; }

    public virtual bool? AutoRenewal { get; private set; }

    public virtual int? TerminationNoticeDays { get; private set; }

    public virtual string? GoverningLaw { get; private set; }

    public virtual string? Summary { get; private set; }

    public virtual ContractStatus Status { get; private set; }

    public virtual double? ExtractionConfidence { get; private set; }

    public virtual bool NeedsReview { get; private set; }

    protected Contract()
    {
    }

    internal Contract(
        Guid id,
        Guid? tenantId,
        Guid documentId,
        string documentTypeCode,
        ExtractedContractFields fields)
        : base(id)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        DocumentTypeCode = documentTypeCode;
        Status = ContractStatus.Draft;

        UpdateExtractedFields(fields);
    }

    public virtual void UpdateExtractedFields(ExtractedContractFields fields)
    {
        Title = fields.Title;
        ContractNumber = fields.ContractNumber;
        PartyAName = fields.PartyAName;
        PartyBName = fields.PartyBName;
        CounterpartyName = fields.CounterpartyName;
        SignedDate = fields.SignedDate;
        EffectiveDate = fields.EffectiveDate;
        ExpirationDate = fields.ExpirationDate;
        TotalAmount = fields.TotalAmount;
        Currency = fields.Currency;
        AutoRenewal = fields.AutoRenewal;
        TerminationNoticeDays = fields.TerminationNoticeDays;
        GoverningLaw = fields.GoverningLaw;
        Summary = fields.Summary;
        ExtractionConfidence = fields.ExtractionConfidence;
        NeedsReview = fields.NeedsReview;
    }

    public virtual void Confirm()
    {
        NeedsReview = false;
        Status = ContractStatus.Active;
    }
}
