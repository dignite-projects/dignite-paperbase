using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var client = new DocumentIntelligenceClient(
            new Uri(_options.Endpoint),
            new AzureKeyCredential(_options.ApiKey));

        BinaryData binaryData;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms);
            binaryData = BinaryData.FromBytes(ms.ToArray());
        }

        var analyzeOptions = new AnalyzeDocumentOptions(_options.ModelId, binaryData)
        {
            // 启用 Markdown 输出（需 api-version 2024-11-30+，SDK 1.0+）。
            // analyzeResult.Content 直接是带标题/表格/列表的 Markdown。
            OutputContentFormat = DocumentContentFormat.Markdown
        };
        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, analyzeOptions);

        var analyzeResult = operation.Value;
        var blocks = new List<OcrBlock>();
        double totalConfidence = 0;
        int blockCount = 0;

        foreach (var page in analyzeResult.Pages ?? [])
        {
            foreach (var line in page.Lines ?? [])
            {
                var confidence = line.Spans?.Any() == true ? 1.0 : 0.9;
                blocks.Add(new OcrBlock
                {
                    Text = line.Content,
                    PageNumber = page.PageNumber,
                    Confidence = confidence,
                    BoundingBox = options.IncludeBlockPositions
                        ? ParseBoundingBox(line.Polygon)
                        : new BoundingBox()
                });
                totalConfidence += confidence;
                blockCount++;
            }
        }

        var markdown = analyzeResult.Content ?? string.Empty;

        return new OcrResult
        {
            RawText = string.Join(Environment.NewLine, blocks.Select(b => b.Text)),
            Markdown = string.IsNullOrEmpty(markdown) ? null : markdown,
            Blocks = blocks,
            Confidence = blockCount > 0 ? totalConfidence / blockCount : 0,
            DetectedLanguage = analyzeResult.Languages?.FirstOrDefault()?.Locale,
            PageCount = analyzeResult.Pages?.Count ?? 0
        };
    }

    protected virtual BoundingBox ParseBoundingBox(IReadOnlyList<float>? polygon)
    {
        // Azure polygon: [x1,y1,x2,y2,x3,y3,x4,y4] — top-left + width/height
        if (polygon == null || polygon.Count < 8)
            return new BoundingBox();

        return new BoundingBox
        {
            X = polygon[0],
            Y = polygon[1],
            Width = polygon[4] - polygon[0],
            Height = polygon[5] - polygon[1]
        };
    }
}
