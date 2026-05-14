using System.Threading.Tasks;

namespace Dignite.Paperbase.Contracts.Extraction;

public interface IContractExtractionCorrectionRecorder
{
    Task RecordAsync(ContractExtractionCorrectionContext context);
}
