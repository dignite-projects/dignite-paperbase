using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Microsoft.AspNetCore.Http;
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

    [HttpGet("export")]
    public virtual Task<IRemoteStreamContent> ExportAsync(GetDocumentListInput input)
    {
        return _documentAppService.ExportAsync(input);
    }

    [HttpPost("{id}/confirm-classification")]
    public virtual Task<DocumentDto> ConfirmClassificationAsync(Guid id, [FromBody] string documentTypeCode)
    {
        return _documentAppService.ConfirmClassificationAsync(id, documentTypeCode);
    }

    [HttpPost("{id}/ask")]
    public virtual Task<QaResultDto> AskAsync(Guid id, [FromBody] AskDocumentInput input)
    {
        return _documentAppService.AskAsync(id, input);
    }

    [HttpPost("bulk-upload")]
    [Consumes("multipart/form-data")]
    public virtual async Task<IReadOnlyList<BulkUploadResultDto>> BulkUploadAsync(IFormFileCollection files)
    {
        var results = new List<BulkUploadResultDto>();

        foreach (var file in files)
        {
            try
            {
                var input = new UploadDocumentInput
                {
                    File = new RemoteStreamContent(file.OpenReadStream(), file.FileName, file.ContentType),
                    FileName = file.FileName
                };
                var doc = await _documentAppService.UploadAsync(input);
                results.Add(new BulkUploadResultDto { FileName = file.FileName, DocumentId = doc.Id, Succeeded = true });
            }
            catch (Exception ex)
            {
                results.Add(new BulkUploadResultDto { FileName = file.FileName, Succeeded = false, ErrorMessage = ex.Message });
            }
        }

        return results;
    }
}
