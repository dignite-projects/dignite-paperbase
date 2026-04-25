using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Documents;

public interface IDocumentRelationAppService : IApplicationService
{
    Task<List<DocumentRelationDto>> GetListAsync(Guid documentId);

    Task<DocumentRelationGraphDto> GetGraphAsync(GetDocumentRelationGraphInput input);

    Task<DocumentRelationDto> CreateAsync(CreateDocumentRelationInput input);

    Task DeleteAsync(Guid id);

    Task<DocumentRelationDto> ConfirmAsync(Guid id);
}
