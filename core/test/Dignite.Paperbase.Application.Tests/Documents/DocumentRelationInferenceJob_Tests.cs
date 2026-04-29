using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Application.Documents.BackgroundJobs;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentRelationInferenceJobTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentKnowledgeIndex>());
        context.Services.AddSingleton(Substitute.For<IDocumentRelationRepository>());

        var workflow = Substitute.ForPartsOf<DocumentRelationInferenceWorkflow>(
            Substitute.For<IChatClient>(),
            Options.Create(new PaperbaseAIOptions { QaTopKChunks = 5 }),
            new DefaultPromptProvider());
        context.Services.AddSingleton(workflow);

        context.Services.Configure<PaperbaseAIOptions>(opt =>
        {
            opt.QaTopKChunks = 5;
        });
    }
}

public class DocumentRelationInferenceJob_Tests : PaperbaseApplicationTestBase<DocumentRelationInferenceJobTestModule>
{
    private readonly DocumentRelationInferenceBackgroundJob _job;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentKnowledgeIndex _knowledgeIndex;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly DocumentRelationInferenceWorkflow _workflow;

    public DocumentRelationInferenceJob_Tests()
    {
        _job = GetRequiredService<DocumentRelationInferenceBackgroundJob>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _knowledgeIndex = GetRequiredService<IDocumentKnowledgeIndex>();
        _relationRepository = GetRequiredService<IDocumentRelationRepository>();
        _workflow = GetRequiredService<DocumentRelationInferenceWorkflow>();

        SetupDocumentSimilarityCapability();
        _knowledgeIndex
            .SearchSimilarDocumentsAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentSimilarityResult>());
    }

    [Fact]
    public async Task Provider_Without_Similar_Document_Search_Skips_Inference()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);
        _knowledgeIndex.Capabilities.Returns(new DocumentKnowledgeIndexCapabilities
        {
            SupportsVectorSearch = true,
            SupportsStructuredFilter = true,
            SupportsDeleteByDocumentId = true,
            NormalizesScore = true,
            SupportsSearchSimilarDocuments = false
        });

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _workflow.DidNotReceive()
            .RunAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<RelationCandidate>>(), Arg.Any<CancellationToken>());

        var latestRun = doc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Skipped);
        latestRun.StatusMessage.ShouldBe("Knowledge index does not support document-level similarity search.");
    }

    [Fact]
    public async Task No_Candidate_Docs_Skips_When_No_Document_Embedding_Found()
    {
        var doc = CreateDocument(extractedText: "契約内容...");
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _workflow.DidNotReceive()
            .RunAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<RelationCandidate>>(), Arg.Any<CancellationToken>());

        var latestRun = doc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Skipped);
        latestRun.StatusMessage.ShouldNotBeNull();
        latestRun.StatusMessage!.ShouldContain("No document embedding found");
    }

    [Fact]
    public async Task Inference_Succeeds_Inserts_Relations_And_Completes_Pipeline()
    {
        var sourceDoc = CreateDocument(extractedText: "業務委託契約書。甲：A社。乙：B社。");
        var candidateDocId = Guid.NewGuid();
        var candidateDoc = CreateDocument(extractedText: "発注書。金額900,000円。");

        _documentRepository.GetAsync(sourceDoc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(sourceDoc);
        _documentRepository.FindAsync(candidateDocId, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(candidateDoc);

        _knowledgeIndex
            .SearchSimilarDocumentsAsync(sourceDoc.Id, sourceDoc.TenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<DocumentSimilarityResult>
            {
                new() { DocumentId = candidateDocId, Score = 0.91 }
            });

        _workflow
            .RunAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<RelationCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(new List<InferredDocumentRelation>
            {
                new() { TargetDocumentId = candidateDocId, Description = "本文档引用了候选文档", Confidence = 0.9 }
            });

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = sourceDoc.Id });

        await _relationRepository.Received(1).InsertAsync(
            Arg.Is<DocumentRelation>(r =>
                r.SourceDocumentId == sourceDoc.Id &&
                r.TargetDocumentId == candidateDocId &&
                r.Description == "本文档引用了候选文档" &&
                r.Source == RelationSource.AiSuggested),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        var latestRun = sourceDoc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Succeeded);
    }

    [Fact]
    public async Task LowConfidence_Relations_Are_Filtered_Before_Persisting()
    {
        var sourceDoc = CreateDocument(extractedText: "業務委託契約書。甲：A社。乙：B社。");
        var highConfidenceId = Guid.NewGuid();
        var lowConfidenceId = Guid.NewGuid();

        _documentRepository.GetAsync(sourceDoc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(sourceDoc);
        _documentRepository.FindAsync(highConfidenceId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(CreateDocument(extractedText: "発注書。金額900,000円。"));
        _documentRepository.FindAsync(lowConfidenceId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(CreateDocument(extractedText: "メモ。特に関係なし。"));

        _knowledgeIndex
            .SearchSimilarDocumentsAsync(sourceDoc.Id, sourceDoc.TenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<DocumentSimilarityResult>
            {
                new() { DocumentId = highConfidenceId, Score = 0.92 },
                new() { DocumentId = lowConfidenceId, Score = 0.88 }
            });

        _workflow
            .RunAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<RelationCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(new List<InferredDocumentRelation>
            {
                new() { TargetDocumentId = highConfidenceId, Description = "本文書は発注書の委託元契約です", Confidence = 0.9 },
                new() { TargetDocumentId = lowConfidenceId, Description = "弱い関係性", Confidence = 0.3 }
            });

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = sourceDoc.Id });

        await _relationRepository.Received(1).InsertAsync(
            Arg.Is<DocumentRelation>(r =>
                r.TargetDocumentId == highConfidenceId &&
                r.Source == RelationSource.AiSuggested),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        await _relationRepository.DidNotReceive().InsertAsync(
            Arg.Is<DocumentRelation>(r => r.TargetDocumentId == lowConfidenceId),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchSimilarDocuments_Is_Called_With_Document_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var doc = CreateDocument(tenantId: tenantId, extractedText: "契約内容...");
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _knowledgeIndex.Received(1)
            .SearchSimilarDocumentsAsync(
                doc.Id,
                tenantId,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>());
    }

    private void SetupDocumentSimilarityCapability()
    {
        _knowledgeIndex.Capabilities.Returns(new DocumentKnowledgeIndexCapabilities
        {
            SupportsVectorSearch = true,
            SupportsStructuredFilter = true,
            SupportsDeleteByDocumentId = true,
            NormalizesScore = true,
            SupportsSearchSimilarDocuments = true
        });
    }

    private static Document CreateDocument(Guid? tenantId = null, string? extractedText = null)
    {
        var doc = new Document(
            Guid.NewGuid(), tenantId,
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));

        if (extractedText != null)
        {
            typeof(Document)
                .GetProperty(nameof(Document.ExtractedText))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(doc, [extractedText]);
        }

        return doc;
    }
}
