using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Abstractions.Chat;

/// <summary>
/// Extension point that lets business modules contribute <see cref="AIFunction"/> tools
/// to document chat conversations scoped to a specific document type.
///
/// <para>
/// <strong>In-process usage (Slice A+B):</strong> implement this interface in a business
/// module's Application layer and register it via ABP's auto-DI
/// (<see cref="Volo.Abp.DependencyInjection.ITransientDependency"/>).
/// <see cref="DocumentChatAppService"/> collects all registered contributors at call time,
/// filters by <see cref="DocumentTypeCode"/>, and adds the returned <see cref="AIFunction"/>
/// instances to the active <c>ChatClientAgent</c>'s tool list.
/// </para>
///
/// <para>
/// <strong>MCP / cross-process expansion (future, backlog):</strong>
/// contributors receive an <see cref="IDocumentChatToolFactory"/> so all in-process tools
/// use the same audit, logging, and metrics behavior. Future cross-process/MCP tools should
/// either be adapted through this factory or provide equivalent instrumentation at the bridge.
/// </para>
/// </summary>
public interface IDocumentChatToolContributor
{
    /// <summary>
    /// The document type code this contributor handles.
    /// Only conversations whose scope matches this code will receive the contributed tools.
    /// Must follow the <c>&lt;owner-module&gt;.&lt;sub-type&gt;</c> naming convention.
    /// </summary>
    string DocumentTypeCode { get; }

    /// <summary>
    /// Returns the <see cref="AIFunction"/> tools to add to the chat agent for the given
    /// conversation context. Called once per turn; the result is merged with the built-in
    /// <c>search_paperbase_documents</c> RAG tool.
    /// </summary>
    IEnumerable<AIFunction> ContributeTools(
        DocumentChatToolContext ctx,
        IDocumentChatToolFactory toolFactory);
}
