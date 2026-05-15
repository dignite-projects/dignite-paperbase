using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentRelationAppServiceTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentRelationRepository>());
    }
}

public class DocumentRelationAppService_Tests
    : PaperbaseApplicationTestBase<DocumentRelationAppServiceTestModule>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentRelationAppService _relationAppService;
    private readonly IDocumentRelationRepository _relationRepository;

    public DocumentRelationAppService_Tests()
    {
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _relationRepository = GetRequiredService<IDocumentRelationRepository>();
        _relationAppService = GetRequiredService<IDocumentRelationAppService>();
    }

    [Fact]
    public async Task GetGraphAsync_Should_Return_Root_And_First_Hop_With_Node_Details()
    {
        var rootId = Guid.NewGuid();
        var firstHopId = Guid.NewGuid();
        var secondHopId = Guid.NewGuid();
        var documents = new List<Document>
        {
            CreateDocument(rootId, "root.pdf", "Root extracted text for summary.", "contract"),
            CreateDocument(firstHopId, "first-hop.pdf", "First hop summary.", "invoice"),
            CreateDocument(secondHopId, "second-hop.pdf", "Second hop summary.", "receipt")
        };
        var relations = new List<DocumentRelation>
        {
            CreateRelation(rootId, firstHopId, source: RelationSource.Manual),
            CreateRelation(firstHopId, secondHopId, source: RelationSource.Manual)
        };

        SetupRepositories(documents, relations);

        var result = await _relationAppService.GetGraphAsync(new GetDocumentRelationGraphInput
        {
            RootDocumentId = rootId,
            Depth = 1
        });

        result.RootDocumentId.ShouldBe(rootId);
        result.Nodes.Select(n => n.DocumentId).ShouldBe(new[] { rootId, firstHopId }, ignoreOrder: true);
        result.Nodes.Single(n => n.DocumentId == rootId).Distance.ShouldBe(0);
        var firstHop = result.Nodes.Single(n => n.DocumentId == firstHopId);
        firstHop.Distance.ShouldBe(1);
        firstHop.Title.ShouldBe("first-hop.pdf");
        firstHop.DocumentTypeCode.ShouldBe("invoice");
        result.Edges.Count.ShouldBe(1);
        result.Edges.Single().TargetDocumentId.ShouldBe(firstHopId);
    }

    [Fact]
    public async Task GetGraphAsync_Should_Expand_To_Requested_Depth_Only()
    {
        var rootId = Guid.NewGuid();
        var firstHopId = Guid.NewGuid();
        var secondHopId = Guid.NewGuid();
        var thirdHopId = Guid.NewGuid();
        var documents = new List<Document>
        {
            CreateDocument(rootId, "root.pdf"),
            CreateDocument(firstHopId, "first-hop.pdf"),
            CreateDocument(secondHopId, "second-hop.pdf"),
            CreateDocument(thirdHopId, "third-hop.pdf")
        };
        var relations = new List<DocumentRelation>
        {
            CreateRelation(rootId, firstHopId),
            CreateRelation(firstHopId, secondHopId),
            CreateRelation(secondHopId, thirdHopId)
        };

        SetupRepositories(documents, relations);

        var result = await _relationAppService.GetGraphAsync(new GetDocumentRelationGraphInput
        {
            RootDocumentId = rootId,
            Depth = 2
        });

        result.Nodes.Select(n => n.DocumentId).ShouldBe(
            new[] { rootId, firstHopId, secondHopId },
            ignoreOrder: true);
        result.Nodes.Single(n => n.DocumentId == secondHopId).Distance.ShouldBe(2);
        result.Nodes.ShouldNotContain(n => n.DocumentId == thirdHopId);
        result.Edges.Select(e => e.TargetDocumentId).ShouldContain(secondHopId);
        result.Edges.ShouldNotContain(e => e.TargetDocumentId == thirdHopId);
    }

    [Fact]
    public async Task GetGraphAsync_Should_Handle_Cycles_Without_Duplicating_Nodes_Or_Edges()
    {
        var rootId = Guid.NewGuid();
        var firstHopId = Guid.NewGuid();
        var secondHopId = Guid.NewGuid();
        var documents = new List<Document>
        {
            CreateDocument(rootId, "root.pdf"),
            CreateDocument(firstHopId, "first-hop.pdf"),
            CreateDocument(secondHopId, "second-hop.pdf")
        };
        var relations = new List<DocumentRelation>
        {
            CreateRelation(rootId, firstHopId),
            CreateRelation(firstHopId, secondHopId),
            CreateRelation(secondHopId, rootId)
        };

        SetupRepositories(documents, relations);

        var result = await _relationAppService.GetGraphAsync(new GetDocumentRelationGraphInput
        {
            RootDocumentId = rootId,
            Depth = 3
        });

        result.Nodes.Select(n => n.DocumentId).Distinct().Count().ShouldBe(result.Nodes.Count);
        result.Edges.Select(e => e.Id).Distinct().Count().ShouldBe(result.Edges.Count);
        result.Nodes.Select(n => n.DocumentId).ShouldBe(
            new[] { rootId, firstHopId, secondHopId },
            ignoreOrder: true);
        result.Edges.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetGraphAsync_Should_Exclude_AiSuggested_When_Disabled()
    {
        var rootId = Guid.NewGuid();
        var manualTargetId = Guid.NewGuid();
        var aiTargetId = Guid.NewGuid();
        var documents = new List<Document>
        {
            CreateDocument(rootId, "root.pdf"),
            CreateDocument(manualTargetId, "manual.pdf"),
            CreateDocument(aiTargetId, "ai.pdf")
        };
        var relations = new List<DocumentRelation>
        {
            CreateRelation(rootId, manualTargetId, source: RelationSource.Manual),
            CreateRelation(rootId, aiTargetId, source: RelationSource.AiSuggested)
        };

        SetupRepositories(documents, relations);

        var result = await _relationAppService.GetGraphAsync(new GetDocumentRelationGraphInput
        {
            RootDocumentId = rootId,
            Depth = 1,
            IncludeAiSuggested = false
        });

        result.Nodes.Select(n => n.DocumentId).ShouldBe(
            new[] { rootId, manualTargetId },
            ignoreOrder: true);
        result.Edges.Count.ShouldBe(1);
        result.Edges.Single().TargetDocumentId.ShouldBe(manualTargetId);
    }

    /// <summary>
    /// Issue #162: 对端 Document 软删除 → 关系仍存在于库中（未级联）但在图中不可见。
    /// 软删 Document 不出现在 GetListByIdsAsync 结果里（ambient ISoftDelete 自动过滤），
    /// GetGraphAsync 据此过滤掉对应的边和节点。
    /// </summary>
    [Fact]
    public async Task GetGraphAsync_Should_Drop_Edge_To_SoftDeleted_Peer()
    {
        var rootId = Guid.NewGuid();
        var aliveTargetId = Guid.NewGuid();
        var deletedTargetId = Guid.NewGuid();
        var documents = new List<Document>
        {
            CreateDocument(rootId, "root.pdf"),
            CreateDocument(aliveTargetId, "alive.pdf")
            // deletedTargetId 故意不出现在 documents 列表中 —— 模拟 ambient
            // ISoftDelete 过滤掉了软删 Document
        };
        var relations = new List<DocumentRelation>
        {
            CreateRelation(rootId, aliveTargetId, source: RelationSource.Manual),
            CreateRelation(rootId, deletedTargetId, source: RelationSource.Manual)
        };

        SetupRepositories(documents, relations);

        var result = await _relationAppService.GetGraphAsync(new GetDocumentRelationGraphInput
        {
            RootDocumentId = rootId,
            Depth = 1
        });

        result.Nodes.Select(n => n.DocumentId).ShouldBe(
            new[] { rootId, aliveTargetId },
            ignoreOrder: true);
        result.Edges.Count.ShouldBe(1);
        result.Edges.Single().TargetDocumentId.ShouldBe(aliveTargetId);
    }

    /// <summary>
    /// Issue #162: 多跳路径上的中间节点软删 → 它和它后续的可达节点都不应在图中渲染。
    /// 中间节点不在 documentById 里，对应边被丢弃；它后续的边的"源端"也不在
    /// documentById 里，也被丢弃。结果：图退化为只剩 root 及 root 的存活直接邻居。
    /// </summary>
    [Fact]
    public async Task GetGraphAsync_Should_Drop_Edges_Whose_Source_Is_SoftDeleted()
    {
        var rootId = Guid.NewGuid();
        var deletedMiddleId = Guid.NewGuid();
        var tailId = Guid.NewGuid();
        var documents = new List<Document>
        {
            CreateDocument(rootId, "root.pdf"),
            CreateDocument(tailId, "tail.pdf")
            // deletedMiddleId 软删除 → 不出现在 documents 列表
        };
        var relations = new List<DocumentRelation>
        {
            CreateRelation(rootId, deletedMiddleId),
            CreateRelation(deletedMiddleId, tailId)
        };

        SetupRepositories(documents, relations);

        var result = await _relationAppService.GetGraphAsync(new GetDocumentRelationGraphInput
        {
            RootDocumentId = rootId,
            Depth = 2
        });

        result.Nodes.Select(n => n.DocumentId).ShouldBe(new[] { rootId });
        result.Edges.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetListAsync_Should_Filter_Out_Relations_With_SoftDeleted_Peer()
    {
        var anchorId = Guid.NewGuid();
        var alivePeerId = Guid.NewGuid();
        var deletedPeerId = Guid.NewGuid();
        var documents = new List<Document>
        {
            CreateDocument(anchorId, "anchor.pdf"),
            CreateDocument(alivePeerId, "alive.pdf")
            // deletedPeerId 软删 → 不出现
        };
        var relations = new List<DocumentRelation>
        {
            CreateRelation(anchorId, alivePeerId, source: RelationSource.Manual),
            CreateRelation(deletedPeerId, anchorId, source: RelationSource.Manual)
        };

        SetupRepositories(documents, relations);
        _relationRepository
            .GetListByDocumentIdAsync(anchorId, Arg.Any<CancellationToken>())
            .Returns(relations);

        var result = await _relationAppService.GetListAsync(anchorId);

        result.Items.Count.ShouldBe(1);
        var visible = result.Items.Single();
        var visiblePeer = visible.SourceDocumentId == anchorId
            ? visible.TargetDocumentId
            : visible.SourceDocumentId;
        visiblePeer.ShouldBe(alivePeerId);
    }

    private void SetupRepositories(
        List<Document> documents,
        List<DocumentRelation> relations)
    {
        _documentRepository
            .GetListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ids = ((IReadOnlyCollection<Guid>)callInfo[0]).ToHashSet();
                return documents.Where(d => ids.Contains(d.Id)).ToList();
            });

        _documentRepository
            .GetAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var id = (Guid)callInfo[0];
                return documents.Single(d => d.Id == id);
            });

        _relationRepository
            .GetListByDocumentIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ids = ((IReadOnlyCollection<Guid>)callInfo[0]).ToHashSet();
                var includeAiSuggested = (bool)callInfo[1];

                return relations
                    .Where(r => ids.Contains(r.SourceDocumentId) || ids.Contains(r.TargetDocumentId))
                    .Where(r => includeAiSuggested || r.Source != RelationSource.AiSuggested)
                    .ToList();
            });
    }

    private static DocumentRelation CreateRelation(
        Guid sourceDocumentId,
        Guid targetDocumentId,
        string description = "测试关系说明",
        RelationSource source = RelationSource.Manual)
    {
        return new DocumentRelation(
            Guid.NewGuid(),
            tenantId: null,
            sourceDocumentId,
            targetDocumentId,
            description,
            source);
    }

    private static Document CreateDocument(
        Guid id,
        string originalFileName,
        string? extractedText = null,
        string? documentTypeCode = null)
    {
        var document = new Document(
            id,
            tenantId: null,
            originalFileBlobName: $"blobs/{originalFileName}",
            sourceType: SourceType.Digital,
            fileOrigin: new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: originalFileName));

        SetProperty(document, nameof(Document.Markdown), extractedText);
        SetProperty(document, nameof(Document.DocumentTypeCode), documentTypeCode);

        return document;
    }

    private static void SetProperty<T>(Document document, string propertyName, T value)
    {
        typeof(Document)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(document, value);
    }
}
