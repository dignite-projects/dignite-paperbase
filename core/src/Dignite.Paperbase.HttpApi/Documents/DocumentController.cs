using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Content;
using Volo.Abp.Application.Dtos;

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
    public virtual Task<PagedResultDto<DocumentDto>> GetListAsync(GetDocumentListInput input)
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
}
