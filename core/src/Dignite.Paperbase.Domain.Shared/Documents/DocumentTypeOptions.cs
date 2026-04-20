using System.Collections.Generic;
using System.Linq;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 文档类型注册表选项。业务模块通过 Configure&lt;DocumentTypeOptions&gt; 注册自己的文档类型。
/// </summary>
public class DocumentTypeOptions
{
    public List<DocumentTypeDefinition> Types { get; } = new();

    public void Register(DocumentTypeDefinition definition)
    {
        var existing = Types.FirstOrDefault(t => t.TypeCode == definition.TypeCode);
        if (existing != null)
        {
            Types.Remove(existing);
        }
        Types.Add(definition);
    }
}
