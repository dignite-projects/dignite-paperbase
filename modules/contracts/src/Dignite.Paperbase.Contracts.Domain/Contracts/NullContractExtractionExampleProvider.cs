using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Contracts.Contracts;

public class NullContractExtractionExampleProvider : IContractExtractionExampleProvider, ITransientDependency
{
    public virtual Task<IReadOnlyList<ContractExtractionExample>> GetExamplesAsync(string documentTypeCode)
    {
        return Task.FromResult<IReadOnlyList<ContractExtractionExample>>([]);
    }
}
