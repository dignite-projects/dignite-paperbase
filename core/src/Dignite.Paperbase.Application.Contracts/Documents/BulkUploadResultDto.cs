using System;

namespace Dignite.Paperbase.Documents;

public class BulkUploadResultDto
{
    public string FileName { get; set; } = default!;
    public Guid? DocumentId { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
