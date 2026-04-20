using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dignite.Paperbase.Application.Documents;

public class DocumentRelationAppService : PaperbaseAppService, IDocumentRelationAppService
{
    private readonly IDocumentRelationRepository _relationRepository;

    public DocumentRelationAppService(IDocumentRelationRepository relationRepository)
    {
        _relationRepository = relationRepository;
    }

    public virtual async Task<List<DocumentRelationDto>> GetListAsync(Guid documentId)
    {
        await CheckPolicyAsync(PaperbasePermissions.DocumentRelations.Default);
        var relations = await _relationRepository.GetListByDocumentIdAsync(documentId);
        return ObjectMapper.Map<List<DocumentRelation>, List<DocumentRelationDto>>(relations);
    }

    [Authorize(PaperbasePermissions.DocumentRelations.Create)]
    public virtual async Task<DocumentRelationDto> CreateAsync(CreateDocumentRelationInput input)
    {
        var relation = new DocumentRelation(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.SourceDocumentId,
            input.TargetDocumentId,
            input.RelationType,
            RelationSource.Manual);

        await _relationRepository.InsertAsync(relation);
        return ObjectMapper.Map<DocumentRelation, DocumentRelationDto>(relation);
    }

    [Authorize(PaperbasePermissions.DocumentRelations.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _relationRepository.DeleteAsync(id);
    }
}
