using System;
using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.Documents.Chat;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Paperbase.Documents.Chat;

public class ChatConversation : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }
    public virtual string Title { get; private set; } = default!;
    public virtual Guid? DocumentId { get; private set; }
    public virtual string? DocumentTypeCode { get; private set; }
    public virtual int? TopK { get; private set; }
    public virtual double? MinScore { get; private set; }

    /// <summary>Serialized MAF AgentSession; stage-1 only, retired in #62.</summary>
    public virtual string? AgentSessionJson { get; private set; }

    private readonly List<ChatMessage> _messages = new();
    public virtual IReadOnlyCollection<ChatMessage> Messages => _messages.AsReadOnly();

    protected ChatConversation() { }

    public ChatConversation(
        Guid id,
        Guid? tenantId,
        string title,
        Guid? documentId,
        string? documentTypeCode,
        int? topK,
        double? minScore)
        : base(id)
    {
        if (documentId.HasValue && !string.IsNullOrEmpty(documentTypeCode))
            throw new BusinessException(PaperbaseErrorCodes.ChatConversationScopeConflict);

        TenantId = tenantId;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: DocumentChatConsts.MaxTitleLength);
        DocumentId = documentId;
        DocumentTypeCode = documentTypeCode;
        TopK = topK;
        MinScore = minScore;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    public virtual void Rename(string title)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: DocumentChatConsts.MaxTitleLength);
        RotateConcurrencyStamp();
    }

    public virtual ChatMessage AppendUserMessage(IClock clock, Guid messageId, string content, Guid clientTurnId)
    {
        if (_messages.Any(m => m.ClientTurnId == clientTurnId))
            throw new BusinessException(PaperbaseErrorCodes.DuplicateClientTurnId);

        var message = new ChatMessage(
            messageId,
            Id,
            ChatMessageRole.User,
            content,
            citationsJson: null,
            clientTurnId,
            clock.Now);

        _messages.Add(message);
        RotateConcurrencyStamp();
        return message;
    }

    public virtual ChatMessage AppendAssistantMessage(IClock clock, Guid messageId, string content, string? citationsJson)
    {
        var message = new ChatMessage(
            messageId,
            Id,
            ChatMessageRole.Assistant,
            content,
            citationsJson,
            clientTurnId: null,
            clock.Now);

        _messages.Add(message);
        RotateConcurrencyStamp();
        return message;
    }

    public virtual void UpdateAgentSession(string? json)
    {
        AgentSessionJson = json;
        RotateConcurrencyStamp();
    }

    protected virtual void RotateConcurrencyStamp()
    {
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
}
