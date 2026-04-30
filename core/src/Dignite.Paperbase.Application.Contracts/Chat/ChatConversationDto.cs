using System;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Chat;

public class ChatConversationDto : FullAuditedEntityDto<Guid>
{
    public Guid? TenantId { get; set; }

    public string Title { get; set; } = default!;

    public Guid? DocumentId { get; set; }

    public string? DocumentTypeCode { get; set; }

    public int? TopK { get; set; }

    public double? MinScore { get; set; }
}
