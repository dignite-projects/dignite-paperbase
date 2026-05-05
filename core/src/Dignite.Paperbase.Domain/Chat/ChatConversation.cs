using System;
using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.Chat;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Paperbase.Chat;

public class ChatConversation : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }
    public virtual string Title { get; private set; } = default!;
    public virtual Guid? DocumentId { get; private set; }
    public virtual string? DocumentTypeCode { get; private set; }
    public virtual int? TopK { get; private set; }
    public virtual double? MinScore { get; private set; }

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
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: ChatConsts.MaxTitleLength);
        DocumentId = documentId;
        DocumentTypeCode = documentTypeCode;
        TopK = topK;
        MinScore = minScore;
        // ConcurrencyStamp is owned by ABP. Manually rotating it here would conflict
        // with AbpDbContext.UpdateConcurrencyStamp, which sets OriginalValue from the
        // entity's current ConcurrencyStamp at save time — pre-rotated entities would
        // produce a WHERE clause that never matches the persisted row.
    }

    public virtual void Rename(string title)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: ChatConsts.MaxTitleLength);
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
            isDegraded: false,
            clientTurnId,
            clock.Now);

        _messages.Add(message);
        return message;
    }

    public virtual ChatMessage AppendAssistantMessage(
        IClock clock,
        Guid messageId,
        string content,
        string? citationsJson,
        bool isDegraded)
    {
        var message = new ChatMessage(
            messageId,
            Id,
            ChatMessageRole.Assistant,
            content,
            citationsJson,
            isDegraded,
            clientTurnId: null,
            clock.Now);

        _messages.Add(message);
        return message;
    }

}
