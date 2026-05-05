using System;
using Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Abstractions.Chat;

/// <summary>
/// Creates document-chat AI tools with the project's shared audit, logging, and metrics behavior.
/// Business modules should use this factory instead of calling <see cref="AIFunctionFactory"/> directly.
/// </summary>
public interface IDocumentChatToolFactory
{
    /// <summary>
    /// Creates an audited document-chat tool from a .NET method delegate.
    /// </summary>
    AIFunction Create(
        DocumentChatToolContext ctx,
        Delegate method,
        string name,
        string description);
}
