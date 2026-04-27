using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;

namespace Dignite.Paperbase.HttpApi.Documents;

[Area("paperbase")]
[Route("api/paperbase/documents")]
public class DocumentController : PaperbaseController, IDocumentAppService
{
    private readonly IDocumentAppService _documentAppService;

    public DocumentController(IDocumentAppService documentAppService)
    {
        _documentAppService = documentAppService;
    }

    [HttpGet("{id}")]
    public virtual Task<DocumentDto> GetAsync(Guid id)
    {
        return _documentAppService.GetAsync(id);
    }

    [HttpGet]
    public virtual Task<PagedResultDto<DocumentListItemDto>> GetListAsync(GetDocumentListInput input)
    {
        return _documentAppService.GetListAsync(input);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public virtual Task<DocumentDto> UploadAsync(UploadDocumentInput input)
    {
        return _documentAppService.UploadAsync(input);
    }

    [HttpGet("{id}/blob")]
    public virtual Task<IRemoteStreamContent> GetBlobAsync(Guid id)
    {
        return _documentAppService.GetBlobAsync(id);
    }

    [HttpDelete("{id}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _documentAppService.DeleteAsync(id);
    }

    [HttpGet("export")]
    public virtual Task<IRemoteStreamContent> GetExportAsync(GetDocumentListInput input)
    {
        return _documentAppService.GetExportAsync(input);
    }

    [HttpPost("{id}/confirm-classification")]
    public virtual Task<DocumentDto> ConfirmClassificationAsync(Guid id, [FromBody] ConfirmClassificationInput input)
    {
        return _documentAppService.ConfirmClassificationAsync(id, input);
    }
}
