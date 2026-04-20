using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Application.Documents;

public class DocumentAppService : PaperbaseAppService, IDocumentAppService
{
    private readonly IDocumentRepository _documentRepository;

    public DocumentAppService(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public virtual async Task<DocumentDto> GetAsync(Guid id)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);
        var document = await _documentRepository.GetAsync(id);
        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    public virtual async Task<PagedResultDto<DocumentDto>> GetListAsync(GetDocumentListInput input)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);

        var totalCount = await _documentRepository.CountAsync();
        var documents = await _documentRepository.GetPagedListAsync(
            input.SkipCount,
            input.MaxResultCount,
            input.Sorting ?? "CreationTime desc");

        return new PagedResultDto<DocumentDto>(
            totalCount,
            ObjectMapper.Map<System.Collections.Generic.List<Document>, System.Collections.Generic.List<DocumentDto>>(documents));
    }

    [Authorize(PaperbasePermissions.Documents.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _documentRepository.DeleteAsync(id);
    }
}
