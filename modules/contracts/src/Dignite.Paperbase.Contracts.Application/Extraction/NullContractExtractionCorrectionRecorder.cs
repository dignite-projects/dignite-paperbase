using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Contracts.Extraction;

public class NullContractExtractionCorrectionRecorder : IContractExtractionCorrectionRecorder, ITransientDependency
{
    public virtual Task RecordAsync(ContractExtractionCorrectionContext context)
    {
        return Task.CompletedTask;
    }
}
