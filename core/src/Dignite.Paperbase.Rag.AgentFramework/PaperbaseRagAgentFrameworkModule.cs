using Dignite.Paperbase.Rag;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Rag.AgentFramework;

/// <summary>
/// Optional bridge module that adapts Paperbase's <see cref="IDocumentVectorStore"/>
/// to Microsoft Agent Framework's <see cref="Microsoft.Agents.AI.TextSearchProvider"/>.
///
/// Reference this module from a host (or feature module) only when you want to wire
/// Paperbase RAG into a <see cref="Microsoft.Agents.AI.ChatClientAgent"/>. The core
/// <c>Dignite.Paperbase.Rag</c> abstraction stays free of any Microsoft.Agents.AI
/// dependency, so providers and traditional QA paths do not pay for the framework
/// when they don't use it.
/// </summary>
[DependsOn(typeof(PaperbaseRagModule))]
public class PaperbaseRagAgentFrameworkModule : AbpModule
{
}
