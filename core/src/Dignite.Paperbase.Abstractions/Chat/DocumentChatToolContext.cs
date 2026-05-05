using System;

namespace Dignite.Paperbase.Abstractions.Chat;

/// <summary>
/// Contextual information passed to <see cref="IDocumentChatToolContributor.ContributeTools"/>
/// so contributors can scope their tool implementations to the right tenant and document type.
/// </summary>
public sealed class DocumentChatToolContext
{
    /// <summary>Document type code that owns this conversation scope.</summary>
    public string? DocumentTypeCode { get; init; }

    /// <summary>Tenant of the conversation. Use this to scope data access inside tool implementations.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Conversation identifier. Useful for per-turn audit logging inside tools.</summary>
    public Guid ConversationId { get; init; }

    /// <summary>Optional single-document scope of the conversation.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>User that initiated the current chat turn.</summary>
    public Guid? UserId { get; init; }
}
