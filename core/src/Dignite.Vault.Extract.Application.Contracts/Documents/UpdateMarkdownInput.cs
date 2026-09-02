using System.ComponentModel.DataAnnotations;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Input for an operator-initiated correction of already-extracted <see cref="Document.Markdown"/> (#555):
/// fixing a small OCR / parsing error, distinct from the pipeline's write-once extraction path.
/// </summary>
public class UpdateMarkdownInput
{
    /// <summary>The corrected Markdown content. Replaces <see cref="Document.Markdown"/> as a whole; no length cap.</summary>
    [Required]
    public string Markdown { get; set; } = default!;

    /// <summary>
    /// <c>true</c> re-runs <b>field extraction only</b> against the corrected Markdown (same mechanism as
    /// <see cref="IDocumentAppService.ReextractFieldsAsync"/>); classification and segmentation are untouched.
    /// <c>false</c> (default) writes the Markdown correction only, with no re-extraction and no event fired.
    /// </summary>
    public bool Reprocess { get; set; }
}
