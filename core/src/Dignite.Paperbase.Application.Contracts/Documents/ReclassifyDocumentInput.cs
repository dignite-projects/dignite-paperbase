using System.ComponentModel.DataAnnotations;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 操作员主动修正文档分类的输入。
/// <para>
/// 与 <see cref="ConfirmClassificationInput"/> 的区别：Confirm 用于 PendingReview 状态下的"确认"
/// 语义；Reclassify 是任意状态下的"操作员认为分类不对，覆写"语义。两者最终都把
/// <see cref="Document.ReviewStatus"/> 落到 Reviewed，但 API 分离便于审计与权限治理。
/// </para>
/// </summary>
public class ReclassifyDocumentInput
{
    [Required]
    [DynamicStringLength(typeof(DocumentConsts), nameof(DocumentConsts.MaxDocumentTypeCodeLength))]
    public string DocumentTypeCode { get; set; } = default!;
}
