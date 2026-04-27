using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Documents;

public interface IDocumentRelationAppService : IApplicationService
{
    Task<ListResultDto<DocumentRelationDto>> GetListAsync(Guid documentId);

    Task<DocumentRelationGraphDto> GetGraphAsync(GetDocumentRelationGraphInput input);

    Task<DocumentRelationDto> CreateAsync(CreateDocumentRelationInput input);

    Task DeleteAsync(Guid id);

    Task<DocumentRelationDto> ConfirmAsync(Guid id);
}
