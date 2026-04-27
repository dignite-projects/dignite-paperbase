using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Documents;

public interface IDocumentQaAppService : IApplicationService
{
    Task<QaResultDto> AskAsync(Guid documentId, AskDocumentInput input);

    Task<QaResultDto> GlobalAskAsync(GlobalAskInput input);
}
