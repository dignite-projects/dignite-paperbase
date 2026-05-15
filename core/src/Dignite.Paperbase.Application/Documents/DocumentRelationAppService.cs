using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Pipelines.RelationDiscovery;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class DocumentRelationAppService : PaperbaseAppService, IDocumentRelationAppService
{
    private const int MaxGraphDepth = 3;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly RelationDiscoveryTelemetryRecorder _telemetry;

    public DocumentRelationAppService(
        IDocumentRepository documentRepository,
        IDocumentRelationRepository relationRepository,
        RelationDiscoveryTelemetryRecorder telemetry)
    {
        _documentRepository = documentRepository;
        _relationRepository = relationRepository;
        _telemetry = telemetry;
    }

    public virtual async Task<ListResultDto<DocumentRelationDto>> GetListAsync(Guid documentId)
    {
        await CheckPolicyAsync(PaperbasePermissions.DocumentRelations.Default);
        var relations = await _relationRepository.GetListByDocumentIdAsync(documentId);

        // Issue #162: 过滤对端已软删除的关系。Document 软删除不级联到 DocumentRelation，
        // 由查询时的对端存活性检查实现"软删 Document 在关系视图中隐身"。
        // 对端查询走 ambient ISoftDelete 过滤，自动只返回未软删的 Document。
        var peerIds = relations
            .Select(r => r.SourceDocumentId == documentId ? r.TargetDocumentId : r.SourceDocumentId)
            .Distinct()
            .ToList();
        var alivePeerIds = (await _documentRepository.GetListByIdsAsync(peerIds))
            .Select(d => d.Id)
            .ToHashSet();
        var visible = relations
            .Where(r => alivePeerIds.Contains(
                r.SourceDocumentId == documentId ? r.TargetDocumentId : r.SourceDocumentId))
            .ToList();

        return new ListResultDto<DocumentRelationDto>(
            ObjectMapper.Map<List<DocumentRelation>, List<DocumentRelationDto>>(visible));
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

        // Issue #162: 软删除的 Document 不在 documentById 中（GetListByIdsAsync 走 ambient
        // ISoftDelete 过滤）。两步过滤：
        // 1) 边：任一端 Document 缺失 → 丢弃，避免悬挂边渲染成 "untitled"。
        // 2) 节点：除 root 外，只保留至少被一条存活边接触到的节点 —— 否则会把
        //    "原本通过死亡中间节点才能到达的下游节点"渲染成无来由的孤岛节点。
        var visibleEdges = edgesById.Values
            .Where(e => documentById.ContainsKey(e.SourceDocumentId)
                && documentById.ContainsKey(e.TargetDocumentId))
            .ToList();

        var reachableNodeIds = new HashSet<Guid> { rootDocument.Id };
        foreach (var edge in visibleEdges)
        {
            reachableNodeIds.Add(edge.SourceDocumentId);
            reachableNodeIds.Add(edge.TargetDocumentId);
        }

        return new DocumentRelationGraphDto
        {
            RootDocumentId = input.RootDocumentId,
            Nodes = distances
                .Where(x => reachableNodeIds.Contains(x.Key))
                .OrderBy(x => x.Value)
                .ThenBy(x => x.Key)
                .Select(x => CreateNodeDto(x.Key, x.Value, documentById))
                .ToList(),
            Edges = visibleEdges
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
        // Issue #123: capture pre-delete source so the funnel metric reflects the ORIGINAL
        // relation kind (a deleted AiSuggested = "user rejected the suggestion"; a deleted
        // Manual = "user undid their own confirmation" — different signals).
        //
        // R2 dismissal tombstone: DocumentRelation is FullAuditedAggregateRoot which implements
        // ISoftDelete — DeleteAsync sets IsDeleted=true rather than physically removing the row.
        // RelationDiscoveryService.GetLinkedPeerDocumentIdsAsync(includeDismissed: true) reads
        // dismissed rows back so the same pair never gets re-suggested. User-facing queries
        // (GetListAsync, GetGraphAsync) honor the ambient soft-delete filter and exclude them.
        var existing = await _relationRepository.FindAsync(id);
        await _relationRepository.DeleteAsync(id);

        if (existing != null)
        {
            _telemetry.RecordSuggestionRejected(existing.Source);
        }
    }

    [Authorize(PaperbasePermissions.DocumentRelations.ConfirmRelation)]
    public virtual async Task<DocumentRelationDto> ConfirmAsync(Guid id)
    {
        var relation = await _relationRepository.GetAsync(id);
        // Capture pre-confirm source; relation.Confirm() flips it to Manual, so the metric
        // needs to be tagged BEFORE the flip.
        var originalSource = relation.Source;

        relation.Confirm();
        await _relationRepository.UpdateAsync(relation);

        _telemetry.RecordSuggestionConfirmed(originalSource);
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
            Title = document?.Title
                ?? document?.FileOrigin.OriginalFileName
                ?? document?.OriginalFileBlobName,
            DocumentTypeCode = document?.DocumentTypeCode,
            LifecycleStatus = document?.LifecycleStatus ?? default,
            ReviewStatus = document?.ReviewStatus ?? default,
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
        };
    }

}
