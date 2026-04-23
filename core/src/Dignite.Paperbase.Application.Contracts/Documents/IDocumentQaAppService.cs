using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Documents;

public interface IDocumentQaAppService : IApplicationService
{
    Task<QaResultDto> GlobalAskAsync(GlobalAskInput input);
}
