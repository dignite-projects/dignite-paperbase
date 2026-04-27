using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Microsoft.AspNetCore.Mvc;

namespace Dignite.Paperbase.HttpApi.Documents;

[Area("paperbase")]
[Route("api/paperbase/document-qa")]
public class DocumentQaController : PaperbaseController, IDocumentQaAppService
{
    private readonly IDocumentQaAppService _documentQaAppService;

    public DocumentQaController(IDocumentQaAppService documentQaAppService)
    {
        _documentQaAppService = documentQaAppService;
    }

    [HttpPost("{documentId}/ask")]
    public virtual Task<QaResultDto> AskAsync(Guid documentId, [FromBody] AskDocumentInput input)
    {
        return _documentQaAppService.AskAsync(documentId, input);
    }

    [HttpPost("global-ask")]
    public virtual Task<QaResultDto> GlobalAskAsync([FromBody] GlobalAskInput input)
    {
        return _documentQaAppService.GlobalAskAsync(input);
    }
}
