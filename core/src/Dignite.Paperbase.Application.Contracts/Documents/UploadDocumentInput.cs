using System.ComponentModel.DataAnnotations;
using Volo.Abp.Content;

namespace Dignite.Paperbase.Documents;

public class UploadDocumentInput
{
    [Required]
    public IRemoteStreamContent File { get; set; } = default!;
}
