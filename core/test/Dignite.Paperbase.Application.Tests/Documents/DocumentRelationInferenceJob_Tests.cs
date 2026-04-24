using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Application.Documents.BackgroundJobs;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Domain.Documents;
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
        context.Services.AddSingleton(Substitute.For<IDocumentChunkRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentRelationRepository>());

        var workflow = Substitute.ForPartsOf<DocumentRelationInferenceWorkflow>(
            Substitute.For<IChatClient>());
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
                new(Guid.NewGuid(), null, doc.Id, 0, "契約内容...", new float[] { 0.1f, 0.2f })
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

        var sourceChunk = new DocumentChunk(Guid.NewGuid(), null, sourceDoc.Id, 0, "業務委託契約書。", new float[] { 0.5f, 0.6f });
        var candidateChunk = new DocumentChunk(Guid.NewGuid(), null, candidateDocId, 0, "発注書。", new float[] { 0.5f, 0.6f });

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
                new() { TargetDocumentId = candidateDocId, RelationType = "references", Confidence = 0.9 }
            });

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = sourceDoc.Id });

        await _relationRepository.Received(1).InsertAsync(
            Arg.Is<DocumentRelation>(r =>
                r.SourceDocumentId == sourceDoc.Id &&
                r.TargetDocumentId == candidateDocId &&
                r.RelationType == "references" &&
                r.Source == RelationSource.AiSuggested),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        var latestRun = sourceDoc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Succeeded);
    }

    private static Document CreateDocument(string? extractedText = null)
    {
        var doc = new Document(
            Guid.NewGuid(), null,
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedAt: new DateTime(2026, 1, 1),
                uploadedByUserId: Guid.NewGuid(),
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
