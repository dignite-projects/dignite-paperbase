using System;

namespace Dignite.Paperbase.Contracts.Contracts;

public class ContractExtractionCorrectionContext
{
    public Guid ContractId { get; set; }

    public Guid DocumentId { get; set; }

    public string DocumentTypeCode { get; set; } = default!;

    public ExtractedContractFields PreviousFields { get; set; } = default!;

    public ExtractedContractFields CorrectedFields { get; set; } = default!;
}
