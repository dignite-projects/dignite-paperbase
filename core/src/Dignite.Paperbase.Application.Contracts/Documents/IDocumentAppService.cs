using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Dignite.Paperbase.Documents;

public interface IDocumentAppService : IApplicationService
{
    Task<DocumentDto> GetAsync(Guid id);

    Task<PagedResultDto<DocumentListItemDto>> GetListAsync(GetDocumentListInput input);

    Task<DocumentDto> UploadAsync(UploadDocumentInput input);

    Task<IRemoteStreamContent> GetBlobAsync(Guid id);

    Task DeleteAsync(Guid id);

    Task<IRemoteStreamContent> GetExportAsync(GetDocumentListInput input);

    Task<DocumentDto> ConfirmClassificationAsync(Guid id, ConfirmClassificationInput input);
}
