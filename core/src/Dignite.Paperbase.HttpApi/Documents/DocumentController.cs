using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Microsoft.AspNetCore.Mvc;
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

    [HttpDelete("{id}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _documentAppService.DeleteAsync(id);
    }
}
