using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dignite.Paperbase.Ocr;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Ocr.PaddleOcr;

public class PaddleOcrProvider : IOcrProvider, ITransientDependency
{
    private readonly PaddleOcrOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public PaddleOcrProvider(
        IOptions<PaddleOcrOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public virtual async Task<OcrResult> RecognizeAsync(Stream fileStream, OcrOptions options)
    {
        var languages = options.LanguageHints.Count > 0
            ? options.LanguageHints
            : _options.Languages;

        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms);
        var fileBytes = ms.ToArray();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(fileBytes), "file", "document");
        content.Add(new StringContent(string.Join(",", languages)), "languages");
        content.Add(new StringContent(_options.ModelName), "model_name");
        content.Add(new StringContent(options.IncludeBlockPositions ? "true" : "false"), "include_bboxes");

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync($"{_options.Endpoint.TrimEnd('/')}/ocr", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaddleOcrResponse>(json)
            ?? throw new InvalidOperationException("PaddleOCR server returned an empty response.");

        var blocks = result.Blocks.Select(b => new OcrBlock
        {
            Text = b.Text,
            Confidence = b.Confidence,
            PageNumber = b.Page,
            BoundingBox = options.IncludeBlockPositions
                ? new BoundingBox { X = b.Bbox[0], Y = b.Bbox[1], Width = b.Bbox[2], Height = b.Bbox[3] }
                : new BoundingBox()
        }).ToList();

        return new OcrResult
        {
            RawText = result.RawText,
            Markdown = string.IsNullOrEmpty(result.Markdown) ? null : result.Markdown,
            Blocks = blocks,
            Confidence = blocks.Count > 0 ? blocks.Average(b => b.Confidence) : 0,
            DetectedLanguage = result.DetectedLanguage,
            PageCount = result.PageCount
        };
    }

    private sealed class PaddleOcrResponse
    {
        [JsonPropertyName("raw_text")]
        public string RawText { get; set; } = string.Empty;

        /// <summary>PP-StructureV3 / PaddleOCR-VL 模型填充；PP-OCRv4 模式下为 null。</summary>
        [JsonPropertyName("markdown")]
        public string? Markdown { get; set; }

        [JsonPropertyName("blocks")]
        public List<PaddleOcrBlock> Blocks { get; set; } = [];

        [JsonPropertyName("detected_language")]
        public string? DetectedLanguage { get; set; }

        [JsonPropertyName("page_count")]
        public int PageCount { get; set; }
    }

    private sealed class PaddleOcrBlock
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }

        // [x, y, width, height]
        [JsonPropertyName("bbox")]
        public double[] Bbox { get; set; } = [0, 0, 0, 0];
    }
}
