using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Documents;

public interface IDocumentAppService : IApplicationService
{
    Task<DocumentDto> GetAsync(Guid id);

    Task<PagedResultDto<DocumentDto>> GetListAsync(GetDocumentListInput input);

    Task DeleteAsync(Guid id);
}
