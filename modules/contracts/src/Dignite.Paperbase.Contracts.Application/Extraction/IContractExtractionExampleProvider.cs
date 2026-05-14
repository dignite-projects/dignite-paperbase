using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Contracts.Extraction;

public interface IContractExtractionExampleProvider
{
    Task<IReadOnlyList<ContractExtractionExample>> GetExamplesAsync(string documentTypeCode);
}
