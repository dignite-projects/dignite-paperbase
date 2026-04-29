using Dignite.Paperbase.Abstractions;
using Dignite.Paperbase.Ocr;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.TextExtraction;

[DependsOn(typeof(PaperbaseAbstractionsModule), typeof(PaperbaseOcrModule))]
public class PaperbaseTextExtractionModule : AbpModule
{
}
