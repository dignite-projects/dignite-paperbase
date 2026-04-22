using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
using Dignite.Paperbase.Abstractions.Ocr;
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

    public virtual async Task<OcrResult> RecognizeAsync(
        Stream fileStream,
        OcrOptions options,
        CancellationToken cancellationToken = default)
    {
        var client = new DocumentIntelligenceClient(
            new Uri(_options.Endpoint),
            new AzureKeyCredential(_options.ApiKey));

        var sw = Stopwatch.StartNew();

        // 将流读入 BinaryData (raw bytes — SDK sends as base64 internally)
        BinaryData binaryData;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms, cancellationToken);
            binaryData = BinaryData.FromBytes(ms.ToArray());
        }

        // Azure.AI.DocumentIntelligence 1.0.0 GA API:
        // - AnalyzeDocumentOptions(modelId, bytesSource) constructor
        // - AnalyzeDocumentAsync(WaitUntil, AnalyzeDocumentOptions, CancellationToken)
        var analyzeOptions = new AnalyzeDocumentOptions(_options.ModelId, binaryData);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            analyzeOptions,
            cancellationToken: cancellationToken);

        sw.Stop();

        var analyzeResult = operation.Value;

        var pages = new List<OcrPage>();
        var allLines = new List<string>();

        if (analyzeResult.Pages != null)
        {
            foreach (var page in analyzeResult.Pages)
            {
                var blocks = new List<OcrBlock>();

                if (page.Lines != null)
                {
                    foreach (var line in page.Lines)
                    {
                        allLines.Add(line.Content);
                        blocks.Add(new OcrBlock
                        {
                            Text = line.Content,
                            Confidence = line.Spans?.Any() == true ? 1.0 : 0.9
                        });
                    }
                }

                pages.Add(new OcrPage
                {
                    PageNumber = page.PageNumber,
                    Blocks = blocks
                });
            }
        }

        var metadata = new Dictionary<string, object>
        {
            ["ProviderName"] = "AzureDocumentIntelligence",
            ["ModelId"] = _options.ModelId,
            ["LatencyMs"] = sw.ElapsedMilliseconds,
            ["PageCount"] = pages.Count
        };

        return new OcrResult
        {
            FullText = string.Join(Environment.NewLine, allLines),
            Pages = pages,
            Metadata = metadata
        };
    }
}
