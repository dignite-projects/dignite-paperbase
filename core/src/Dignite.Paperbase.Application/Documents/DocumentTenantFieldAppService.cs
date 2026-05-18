using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Entities;

namespace Dignite.Paperbase.Documents;

[Authorize(PaperbasePermissions.Documents.Default)]
public class DocumentTenantFieldAppService : PaperbaseAppService, IDocumentTenantFieldAppService
{
    private readonly IDocumentTenantFieldRepository _repository;
    private readonly IDocumentRepository _documentRepository;

    public DocumentTenantFieldAppService(
        IDocumentTenantFieldRepository repository,
        IDocumentRepository documentRepository)
    {
        _repository = repository;
        _documentRepository = documentRepository;
    }

    public virtual async Task<List<DocumentTenantFieldDto>> GetByDocumentAsync(Guid documentId)
    {
        // 显式租户断言：先验证 Document 归当前租户
        var document = await _documentRepository.FindAsync(documentId, includeDetails: false);
        if (document == null || document.TenantId != CurrentTenant.Id)
        {
            throw new EntityNotFoundException(typeof(Document), documentId);
        }

        var fields = await _repository.GetByDocumentAsync(CurrentTenant.Id, documentId);
        return fields.Select(f => new DocumentTenantFieldDto
        {
            Id = f.Id,
            TenantId = f.TenantId,
            DocumentId = f.DocumentId,
            FieldName = f.FieldName,
            Value = f.Value,
            Confidence = f.Confidence
        }).ToList();
    }
}
