using System;
using Dignite.Paperbase.Contracts;

namespace Dignite.Paperbase.Contracts.Extraction;

public class ContractExtractionCorrectionContext
{
    public Guid ContractId { get; set; }

    public Guid DocumentId { get; set; }

    public string DocumentTypeCode { get; set; } = default!;

    public ContractFields PreviousFields { get; set; } = default!;

    public ContractFields CorrectedFields { get; set; } = default!;
}
