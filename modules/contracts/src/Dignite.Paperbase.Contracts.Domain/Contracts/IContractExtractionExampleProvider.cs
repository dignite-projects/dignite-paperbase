using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Contracts.Contracts;

public interface IContractExtractionExampleProvider
{
    Task<IReadOnlyList<ContractExtractionExample>> GetExamplesAsync(string documentTypeCode);
}
