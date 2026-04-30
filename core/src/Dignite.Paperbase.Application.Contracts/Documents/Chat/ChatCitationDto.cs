using System;

namespace Dignite.Paperbase.Documents.Chat;

public class ChatCitationDto
{
    public Guid DocumentId { get; set; }

    public int? PageNumber { get; set; }

    public int? ChunkIndex { get; set; }

    public string Snippet { get; set; } = default!;

    public string SourceName { get; set; } = default!;
}
