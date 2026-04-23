using Dignite.Paperbase.Abstractions;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.AI;

/// <summary>
/// AI 能力模块。仅依赖 Abstractions + Caching，不依赖 Domain / Application / EF Core。
/// 宿主应用须先注册 IChatClient（Azure OpenAI 或 Ollama），本模块在其上增加审计封装。
/// </summary>
[DependsOn(
    typeof(PaperbaseAbstractionsModule),
    typeof(AbpCachingModule)
)]
public class PaperbaseAIModule : AbpModule
{
}
