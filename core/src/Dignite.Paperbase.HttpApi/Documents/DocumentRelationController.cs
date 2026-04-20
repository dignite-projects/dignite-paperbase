using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Microsoft.AspNetCore.Mvc;

namespace Dignite.Paperbase.HttpApi.Documents;

[Area("paperbase")]
[Route("api/paperbase/document-relations")]
public class DocumentRelationController : PaperbaseController, IDocumentRelationAppService
{
    private readonly IDocumentRelationAppService _relationAppService;

    public DocumentRelationController(IDocumentRelationAppService relationAppService)
    {
        _relationAppService = relationAppService;
    }

    [HttpGet]
    public virtual Task<List<DocumentRelationDto>> GetListAsync(Guid documentId)
    {
        return _relationAppService.GetListAsync(documentId);
    }

    [HttpPost]
    public virtual Task<DocumentRelationDto> CreateAsync(CreateDocumentRelationInput input)
    {
        return _relationAppService.CreateAsync(input);
    }

    [HttpDelete("{id}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _relationAppService.DeleteAsync(id);
    }
}
