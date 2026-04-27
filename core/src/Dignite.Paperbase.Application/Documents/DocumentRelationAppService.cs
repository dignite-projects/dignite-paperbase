using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Application.Documents;

public class DocumentRelationAppService : PaperbaseAppService, IDocumentRelationAppService
{
    private const int MaxGraphDepth = 3;
    private const int SummaryLength = 300;

    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentRelationRepository _relationRepository;

    public DocumentRelationAppService(
        IDocumentRepository documentRepository,
        IDocumentRelationRepository relationRepository)
    {
        _documentRepository = documentRepository;
        _relationRepository = relationRepository;
    }

    public virtual async Task<ListResultDto<DocumentRelationDto>> GetListAsync(Guid documentId)
    {
        await CheckPolicyAsync(PaperbasePermissions.DocumentRelations.Default);
        var relations = await _relationRepository.GetListByDocumentIdAsync(documentId);
        return new ListResultDto<DocumentRelationDto>(
            ObjectMapper.Map<List<DocumentRelation>, List<DocumentRelationDto>>(relations));
    }

    public virtual async Task<DocumentRelationGraphDto> GetGraphAsync(GetDocumentRelationGraphInput input)
    {
        await CheckPolicyAsync(PaperbasePermissions.DocumentRelations.Default);

        if (input.RootDocumentId == Guid.Empty)
        {
            throw new ArgumentException("RootDocumentId can not be empty.", nameof(input.RootDocumentId));
        }

        if (input.Depth is < 1 or > MaxGraphDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.Depth),
                input.Depth,
                $"Depth must be between 1 and {MaxGraphDepth}.");
        }

        var rootDocument = await _documentRepository.GetAsync(input.RootDocumentId);
        var distances = new Dictionary<Guid, int>
        {
            [input.RootDocumentId] = 0
        };
        var frontier = new HashSet<Guid> { input.RootDocumentId };
        var edgesById = new Dictionary<Guid, DocumentRelation>();

        for (var distance = 1; distance <= input.Depth && frontier.Count > 0; distance++)
        {
            var relations = await _relationRepository.GetListByDocumentIdsAsync(
                frontier.ToList(),
                input.IncludeAiSuggested);

            var nextFrontier = new HashSet<Guid>();
            foreach (var relation in relations)
            {
                edgesById.TryAdd(relation.Id, relation);

                AddNeighborIfDiscoveredFromFrontier(
                    relation.SourceDocumentId,
                    relation.TargetDocumentId,
                    frontier,
                    nextFrontier,
                    distances,
                    distance);

                AddNeighborIfDiscoveredFromFrontier(
                    relation.TargetDocumentId,
                    relation.SourceDocumentId,
                    frontier,
                    nextFrontier,
                    distances,
                    distance);
            }

            frontier = nextFrontier;
        }

        var documents = await _documentRepository.GetListByIdsAsync(distances.Keys.ToList());
        var documentById = documents.ToDictionary(d => d.Id);
        documentById[rootDocument.Id] = rootDocument;

        return new DocumentRelationGraphDto
        {
            RootDocumentId = input.RootDocumentId,
            Nodes = distances
                .OrderBy(x => x.Value)
                .ThenBy(x => x.Key)
                .Select(x => CreateNodeDto(x.Key, x.Value, documentById))
                .ToList(),
            Edges = edgesById.Values
                .OrderBy(e => e.CreationTime)
                .ThenBy(e => e.Id)
                .Select(CreateEdgeDto)
                .ToList()
        };
    }

    [Authorize(PaperbasePermissions.DocumentRelations.Create)]
    public virtual async Task<DocumentRelationDto> CreateAsync(CreateDocumentRelationInput input)
    {
        var relation = new DocumentRelation(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.SourceDocumentId,
            input.TargetDocumentId,
            input.Description,
            RelationSource.Manual);

        await _relationRepository.InsertAsync(relation);
        return ObjectMapper.Map<DocumentRelation, DocumentRelationDto>(relation);
    }

    [Authorize(PaperbasePermissions.DocumentRelations.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _relationRepository.DeleteAsync(id);
    }

    [Authorize(PaperbasePermissions.DocumentRelations.ConfirmRelation)]
    public virtual async Task<DocumentRelationDto> ConfirmAsync(Guid id)
    {
        var relation = await _relationRepository.GetAsync(id);
        relation.Confirm();
        await _relationRepository.UpdateAsync(relation);
        return ObjectMapper.Map<DocumentRelation, DocumentRelationDto>(relation);
    }

    private static void AddNeighborIfDiscoveredFromFrontier(
        Guid currentDocumentId,
        Guid neighborDocumentId,
        HashSet<Guid> frontier,
        HashSet<Guid> nextFrontier,
        Dictionary<Guid, int> distances,
        int distance)
    {
        if (!frontier.Contains(currentDocumentId) || distances.ContainsKey(neighborDocumentId))
        {
            return;
        }

        distances[neighborDocumentId] = distance;
        nextFrontier.Add(neighborDocumentId);
    }

    private static DocumentRelationNodeDto CreateNodeDto(
        Guid documentId,
        int distance,
        Dictionary<Guid, Document> documentById)
    {
        documentById.TryGetValue(documentId, out var document);

        return new DocumentRelationNodeDto
        {
            DocumentId = documentId,
            Title = document?.FileOrigin.OriginalFileName ?? document?.OriginalFileBlobName,
            DocumentTypeCode = document?.DocumentTypeCode,
            LifecycleStatus = document?.LifecycleStatus ?? default,
            ReviewStatus = document?.ReviewStatus ?? default,
            Summary = CreateSummary(document?.ExtractedText),
            Distance = distance
        };
    }

    private static DocumentRelationEdgeDto CreateEdgeDto(DocumentRelation relation)
    {
        return new DocumentRelationEdgeDto
        {
            Id = relation.Id,
            SourceDocumentId = relation.SourceDocumentId,
            TargetDocumentId = relation.TargetDocumentId,
            Description = relation.Description,
            Source = relation.Source,
            Confidence = relation.Confidence
        };
    }

    private static string? CreateSummary(string? extractedText)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return null;
        }

        var trimmed = extractedText.Trim();
        return trimmed.Length <= SummaryLength
            ? trimmed
            : trimmed[..SummaryLength];
    }
}
