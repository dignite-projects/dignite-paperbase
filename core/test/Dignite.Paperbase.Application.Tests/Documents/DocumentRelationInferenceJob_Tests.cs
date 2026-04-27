using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Application.Documents.BackgroundJobs;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pgvector;
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
        context.Services.AddSingleton(Substitute.For<IDocumentChunkRepository>());
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
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly DocumentRelationInferenceWorkflow _workflow;

    public DocumentRelationInferenceJob_Tests()
    {
        _job = GetRequiredService<DocumentRelationInferenceBackgroundJob>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _chunkRepository = GetRequiredService<IDocumentChunkRepository>();
        _relationRepository = GetRequiredService<IDocumentRelationRepository>();
        _workflow = GetRequiredService<DocumentRelationInferenceWorkflow>();

        _chunkRepository
            .GetListByDocumentIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());
        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());
    }

    [Fact]
    public async Task No_Source_Chunks_Skips_Inference()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);
        _chunkRepository
            .GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _workflow.DidNotReceive()
            .RunAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<RelationCandidate>>(), Arg.Any<CancellationToken>());

        var latestRun = doc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Skipped);
        latestRun.StatusMessage.ShouldBe("No chunks found.");
    }

    [Fact]
    public async Task No_Candidate_Docs_Completes_With_NoCandidates()
    {
        var doc = CreateDocument(extractedText: "契約内容...");
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);

        _chunkRepository
            .GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>
            {
                new(Guid.NewGuid(), null, doc.Id, 0, "契約内容...", new Vector(new float[PaperbaseDbProperties.EmbeddingVectorDimension]))
            });

        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _workflow.DidNotReceive()
            .RunAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<RelationCandidate>>(), Arg.Any<CancellationToken>());

        var latestRun = doc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Succeeded);
        // NoCandidates signal is expressed by absence of DocumentRelation records, not on the Run
        await _relationRepository.DidNotReceive().InsertAsync(
            Arg.Any<DocumentRelation>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inference_Succeeds_Inserts_Relations_And_Completes_Pipeline()
    {
        var sourceDoc = CreateDocument(extractedText: "業務委託契約書。甲：A社。乙：B社。");
        var candidateDocId = Guid.NewGuid();
        var candidateDoc = CreateDocument(extractedText: "発注書。金額900,000円。");

        _documentRepository.GetAsync(sourceDoc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(sourceDoc);
        _documentRepository.FindAsync(candidateDocId, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(candidateDoc);

        var sourceChunk = new DocumentChunk(Guid.NewGuid(), null, sourceDoc.Id, 0, "業務委託契約書。", new Vector(new float[PaperbaseDbProperties.EmbeddingVectorDimension]));
        var candidateChunk = new DocumentChunk(Guid.NewGuid(), null, candidateDocId, 0, "発注書。", new Vector(new float[PaperbaseDbProperties.EmbeddingVectorDimension]));

        _chunkRepository
            .GetListByDocumentIdAsync(sourceDoc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk> { sourceChunk });

        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk> { candidateChunk });

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

        var sourceChunk = new DocumentChunk(Guid.NewGuid(), null, sourceDoc.Id, 0, "業務委託契約書。",
            new Vector(new float[PaperbaseDbProperties.EmbeddingVectorDimension]));
        var highChunk = new DocumentChunk(Guid.NewGuid(), null, highConfidenceId, 0, "発注書。",
            new Vector(new float[PaperbaseDbProperties.EmbeddingVectorDimension]));
        var lowChunk = new DocumentChunk(Guid.NewGuid(), null, lowConfidenceId, 0, "メモ。",
            new Vector(new float[PaperbaseDbProperties.EmbeddingVectorDimension]));

        _chunkRepository
            .GetListByDocumentIdAsync(sourceDoc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk> { sourceChunk });

        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk> { highChunk, lowChunk });

        // Workflow returns two results: one above threshold (0.9), one below (0.3)
        _workflow
            .RunAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<RelationCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(new List<InferredDocumentRelation>
            {
                new() { TargetDocumentId = highConfidenceId, Description = "本文書は発注書の委託元契約です", Confidence = 0.9 },
                new() { TargetDocumentId = lowConfidenceId, Description = "弱い関係性", Confidence = 0.3 }
            });

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = sourceDoc.Id });

        // Only the high-confidence relation reaches the aggregate
        await _relationRepository.Received(1).InsertAsync(
            Arg.Is<DocumentRelation>(r =>
                r.TargetDocumentId == highConfidenceId &&
                r.Source == RelationSource.AiSuggested),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        // The 0.3-confidence relation must never reach the aggregate, even if LLM ignores the prompt rule
        await _relationRepository.DidNotReceive().InsertAsync(
            Arg.Is<DocumentRelation>(r => r.TargetDocumentId == lowConfidenceId),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MeanPooled_Vector_Is_Used_For_Candidate_Retrieval()
    {
        var doc = CreateDocument(extractedText: "契約内容...");
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);

        var vec1 = new float[PaperbaseDbProperties.EmbeddingVectorDimension];
        var vec2 = new float[PaperbaseDbProperties.EmbeddingVectorDimension];
        vec1[0] = 2.0f;
        vec2[0] = 4.0f;

        _chunkRepository
            .GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>
            {
                new(Guid.NewGuid(), null, doc.Id, 0, "chunk1", new Vector(vec1)),
                new(Guid.NewGuid(), null, doc.Id, 1, "chunk2", new Vector(vec2))
            });

        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _chunkRepository.Received(1).SearchByVectorAsync(
            Arg.Is<float[]>(v => Math.Abs(v[0] - 3.0f) < 1e-5f),
            Arg.Any<int>(),
            Arg.Any<Guid?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private static Document CreateDocument(string? extractedText = null)
    {
        var doc = new Document(
            Guid.NewGuid(), null,
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
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
