using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Application.Documents.BackgroundJobs;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Paperbase.Application.Documents;

public class DocumentAppService : PaperbaseAppService, IDocumentAppService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly DocumentQaWorkflow _qaWorkflow;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentAppService(
        IDocumentRepository documentRepository,
        IBlobContainer<PaperbaseDocumentContainer> blobContainer,
        IBackgroundJobManager backgroundJobManager,
        DocumentPipelineRunManager pipelineRunManager,
        IDistributedEventBus distributedEventBus,
        DocumentQaWorkflow qaWorkflow,
        IDocumentChunkRepository chunkRepository,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _documentRepository = documentRepository;
        _blobContainer = blobContainer;
        _backgroundJobManager = backgroundJobManager;
        _pipelineRunManager = pipelineRunManager;
        _distributedEventBus = distributedEventBus;
        _qaWorkflow = qaWorkflow;
        _chunkRepository = chunkRepository;
        _embeddingGenerator = embeddingGenerator;
        _aiOptions = aiOptions.Value;
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

        var query = await _documentRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);

        var totalCount = await AsyncExecuter.CountAsync(query);

        query = ApplySorting(query, input.Sorting);
        query = query.Skip(input.SkipCount).Take(input.MaxResultCount);

        var documents = await AsyncExecuter.ToListAsync(query);

        return new PagedResultDto<DocumentDto>(
            totalCount,
            ObjectMapper.Map<List<Document>, List<DocumentDto>>(documents));
    }

    [Authorize(PaperbasePermissions.Documents.Upload)]
    public virtual async Task<DocumentDto> UploadAsync(UploadDocumentInput input)
    {
        var fileName = input.File.FileName ?? "document";
        var contentType = input.File.ContentType ?? "application/octet-stream";
        var fileSize = input.File.ContentLength ?? 0;
        var extension = Path.GetExtension(fileName);
        var blobName = GuidGenerator.Create().ToString("N") + extension;

        await using var stream = input.File.GetStream();
        await _blobContainer.SaveAsync(blobName, stream);

        var sourceType = SourceType.Physical; // placeholder；提取完成后由 BackgroundJob 回写实际值
        var fileOrigin = new FileOrigin(
            CurrentUser.UserName ?? string.Empty,
            contentType,
            fileSize,
            originalFileName: fileName);

        var document = new Document(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            blobName,
            sourceType,
            fileOrigin);

        await _documentRepository.InsertAsync(document, autoSave: true);

        await _backgroundJobManager.EnqueueAsync(
            new DocumentTextExtractionJobArgs { DocumentId = document.Id });

        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    public virtual async Task<IRemoteStreamContent> GetBlobAsync(Guid id)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);

        var document = await _documentRepository.GetAsync(id);
        var stream = await _blobContainer.GetAsync(document.OriginalFileBlobName);

        return new RemoteStreamContent(
            stream,
            document.FileOrigin.OriginalFileName,
            document.FileOrigin.ContentType,
            disposeStream: true);
    }

    [Authorize(PaperbasePermissions.Documents.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var document = await _documentRepository.GetAsync(id);
        await _blobContainer.DeleteAsync(document.OriginalFileBlobName);
        await _documentRepository.DeleteAsync(id);
    }

    [Authorize(PaperbasePermissions.Documents.Export)]
    public virtual async Task<IRemoteStreamContent> ExportAsync(GetDocumentListInput input)
    {
        var query = await _documentRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);
        query = ApplySorting(query, input.Sorting);

        var documents = await AsyncExecuter.ToListAsync(query);
        var csv = BuildDocumentCsv(documents);
        var bytes = Encoding.UTF8.GetBytes(csv);

        return new RemoteStreamContent(new MemoryStream(bytes), "documents.csv", "text/csv");
    }

    [Authorize(PaperbasePermissions.Documents.ConfirmClassification)]
    public virtual async Task<DocumentDto> ConfirmClassificationAsync(Guid id, string documentTypeCode)
    {
        var document = await _documentRepository.GetAsync(id);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification);

        await _pipelineRunManager.CompleteManualClassificationAsync(document, run, documentTypeCode);

        await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
        {
            DocumentId = document.Id,
            TenantId = document.TenantId,
            DocumentTypeCode = documentTypeCode,
            ConfidenceScore = 1.0,
            ExtractedText = document.ExtractedText
        });

        await _backgroundJobManager.EnqueueAsync(
            new DocumentEmbeddingJobArgs { DocumentId = document.Id });

        await _documentRepository.UpdateAsync(document);
        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    [Authorize(PaperbasePermissions.Documents.Ask)]
    public virtual async Task<QaResultDto> AskAsync(Guid id, AskDocumentInput input)
    {
        var document = await _documentRepository.GetAsync(id);

        var actualMode = DetermineActualMode(input.Mode, document.HasEmbedding);

        DocumentQaOutcome outcome;
        if (actualMode == QaMode.Rag)
        {
            var questionEmbeddings = await _embeddingGenerator.GenerateAsync([input.Question]);
            var chunks = await _chunkRepository.SearchByVectorAsync(
                questionEmbeddings[0].Vector.ToArray(), _aiOptions.QaTopKChunks, documentId: id);

            var qaChunks = chunks.Select(c => new QaChunk
            {
                ChunkIndex = c.ChunkIndex,
                ChunkText = c.ChunkText
            }).ToList();

            outcome = await _qaWorkflow.RunRagAsync(input.Question, qaChunks);
        }
        else
        {
            outcome = await _qaWorkflow.RunFullTextAsync(input.Question, document.ExtractedText);
        }

        return new QaResultDto
        {
            Answer = outcome.Answer,
            ActualMode = outcome.ActualMode.ToString(),
            IsDegraded = input.Mode == QaMode.Auto && !document.HasEmbedding,
            Sources = outcome.Sources.Select(s => new QaSourceDto
            {
                Text = s.Text,
                ChunkIndex = s.ChunkIndex
            }).ToList()
        };
    }

    private static QaMode DetermineActualMode(QaMode requested, bool hasEmbedding)
    {
        if (requested == QaMode.Rag) return QaMode.Rag;
        if (requested == QaMode.FullText) return QaMode.FullText;
        return hasEmbedding ? QaMode.Rag : QaMode.FullText;
    }

    protected virtual IQueryable<Document> ApplyFilter(IQueryable<Document> query, GetDocumentListInput input)
    {
        if (input.LifecycleStatus.HasValue)
            query = query.Where(x => x.LifecycleStatus == input.LifecycleStatus.Value);

        if (!input.DocumentTypeCode.IsNullOrWhiteSpace())
            query = query.Where(x => x.DocumentTypeCode == input.DocumentTypeCode);

        if (input.ReviewStatus.HasValue)
            query = query.Where(d => d.ReviewStatus == input.ReviewStatus.Value);

        return query;
    }

    protected virtual IQueryable<Document> ApplySorting(IQueryable<Document> query, string? sorting)
    {
        return sorting switch
        {
            "creationTime" => query.OrderBy(x => x.CreationTime),
            _ => query.OrderByDescending(x => x.CreationTime)
        };
    }

    private static string BuildDocumentCsv(List<Document> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,DocumentTypeCode,LifecycleStatus,OriginalFileName,ContentType,CreationTime");

        foreach (var d in documents)
        {
            sb.AppendLine(string.Join(",",
                d.Id,
                EscapeCsv(d.DocumentTypeCode),
                d.LifecycleStatus.ToString(),
                EscapeCsv(d.FileOrigin.OriginalFileName),
                EscapeCsv(d.FileOrigin.ContentType),
                d.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (value.IsNullOrEmpty()) return string.Empty;
        if (value!.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
