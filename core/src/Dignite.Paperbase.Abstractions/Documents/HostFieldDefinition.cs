using Volo.Abp;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// Host 部署者在 <see cref="DocumentTypeDefinition"/> 下注册的类型绑定字段定义。
/// 抽取时 LLM 按 <see cref="Prompt"/> 从文档 Markdown 中取值，写入 <c>Document.SystemFieldsJson</c>。
/// <para>
/// 与租户字段（B 机制，#169）的区别：
/// <list type="bullet">
///   <item><b>Host 字段</b> 在 Host 部署级注册，所有租户共享（例：医疗病历 type 下的"科室"字段）</item>
///   <item><b>租户字段</b> per-tenant 配置，租户私有（例：租户 X 在合同 type 下加"项目代码"）</item>
/// </list>
/// 两者共享 LLM 抽取引擎；分开两路是因为 Host 字段属"部署即生效"的统一配置，租户字段属"运营时定制"。
/// </para>
/// </summary>
public class HostFieldDefinition
{
    public string Name { get; }

    /// <summary>
    /// LLM 抽取指令——告诉模型从文档中找什么值。例如：
    /// <c>"Extract the contract total amount as a decimal number"</c>。
    /// 必须是<b>编译期常量或纯静态字符串字面量</b>——不能拼接用户输入（防 prompt injection）。
    /// </summary>
    public string Prompt { get; }

    public FieldDataType DataType { get; }

    /// <summary>抽取失败时是否标记为"待人工补全"。默认 false（缺值即视为 null）。</summary>
    public bool Required { get; }

    public HostFieldDefinition(string name, string prompt, FieldDataType dataType, bool required = false)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name));
        Prompt = Check.NotNullOrWhiteSpace(prompt, nameof(prompt));
        DataType = dataType;
        Required = required;
    }
}
