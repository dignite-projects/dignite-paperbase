using System.Threading.Tasks;

namespace Dignite.Paperbase.Contracts.Contracts;

public interface IContractExtractionCorrectionRecorder
{
    Task RecordAsync(ContractExtractionCorrectionContext context);
}
