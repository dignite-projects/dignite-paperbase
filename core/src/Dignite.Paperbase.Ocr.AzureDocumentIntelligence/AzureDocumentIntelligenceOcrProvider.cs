using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
using Dignite.Paperbase.Ocr;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Ocr.AzureDocumentIntelligence;

public class AzureDocumentIntelligenceOcrProvider : IOcrProvider, ITransientDependency
{
    private readonly AzureDocumentIntelligenceOptions _options;

    public AzureDocumentIntelligenceOcrProvider(IOptions<AzureDocumentIntelligenceOptions> options)
    {
        _options = options.Value;
    }

    public virtual async Task<OcrResult> RecognizeAsync(Stream fileStream, OcrOptions options)
    {
        var modelId = ResolveModelId(options.OcrProfileCode);
        var analyzeResult = await AnalyzeAsync(fileStream, modelId, cancellationToken: default);

        var markdown = BuildMarkdown(analyzeResult);
        var confidence = CalculateConfidence(analyzeResult);
        var pageCount = analyzeResult.Pages?.Count ?? 0;

        return new OcrResult
        {
            Markdown = markdown,
            Confidence = confidence,
            DetectedLanguage = analyzeResult.Languages?.FirstOrDefault()?.Locale,
            PageCount = pageCount,
            AppliedProfileCode = options.OcrProfileCode,
            ProviderName = "AzureDocumentIntelligence",
            ProviderModelName = modelId,
            ProviderVersion = typeof(DocumentIntelligenceClient).Assembly.GetName().Version?.ToString(),
            QualitySignals = OcrQualitySignalBuilder.FromMarkdown(markdown, confidence, pageCount)
        };
    }

    private async Task<AnalyzeResult> AnalyzeAsync(
        Stream fileStream,
        string modelId,
        CancellationToken cancellationToken)
    {
        var client = new DocumentIntelligenceClient(
            new Uri(_options.Endpoint),
            new AzureKeyCredential(_options.ApiKey));

        BinaryData binaryData;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms, cancellationToken);
            binaryData = BinaryData.FromBytes(ms.ToArray());
        }

        var analyzeOptions = new AnalyzeDocumentOptions(modelId, binaryData)
        {
            // 启用 Markdown 输出（需 api-version 2024-11-30+，SDK 1.0+）。
            // analyzeResult.Content 直接是带标题/表格/列表的 Markdown。
            OutputContentFormat = DocumentContentFormat.Markdown
        };

        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, analyzeOptions, cancellationToken);
        var analyzeResult = operation.Value;
        return analyzeResult;
    }

    private string ResolveModelId(string? profileCode)
    {
        var normalized = OcrProfileCodes.Normalize(profileCode);
        if (normalized == OcrProfileCodes.Auto)
        {
            return _options.ModelId;
        }

        return _options.ProfileModelIds.TryGetValue(normalized, out var modelId) &&
               !string.IsNullOrWhiteSpace(modelId)
            ? modelId
            : _options.ModelId;
    }

    private static double CalculateConfidence(AnalyzeResult analyzeResult)
    {
        // Confidence: page.Words[*].Confidence 是 Azure DI 真实给的字符级 softmax 评分，
        // 取所有词的均值作为整体识别置信度。DocumentLine 自身不携带 confidence，
        // 早期实现按 Spans 命中兜底成 0.9/1.0 是假评分，会让门槛检查事实上变成 no-op——
        // 现已切回 SDK 真实字段。
        double totalConfidence = 0;
        int wordCount = 0;
        foreach (var page in analyzeResult.Pages ?? [])
        {
            foreach (var word in page.Words ?? [])
            {
                totalConfidence += word.Confidence;
                wordCount++;
            }
        }

        return wordCount > 0 ? totalConfidence / wordCount : 0;
    }

    private static string BuildMarkdown(AnalyzeResult analyzeResult)
    {
        // analyzeResult.Content 已是 Markdown；若 Azure 返回空，回退到行级文本拼接成扁平 Markdown 段落。
        // Provider 负责自填，不把 plain-text-to-markdown 翻译职责泄漏给上游 orchestrator。
        var markdown = analyzeResult.Content;
        if (string.IsNullOrEmpty(markdown))
        {
            var paragraphs = (analyzeResult.Pages ?? [])
                .SelectMany(p => p.Lines ?? [])
                .Select(l => l.Content)
                .Where(t => !string.IsNullOrWhiteSpace(t));
            markdown = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        }

        return markdown ?? string.Empty;
    }
}
