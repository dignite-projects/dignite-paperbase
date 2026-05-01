namespace Dignite.Paperbase.Ai;

/// <summary>
/// Workflow 系统提示词模板，由 <see cref="IPromptProvider"/> 返回。
/// </summary>
/// <param name="SystemInstructions">
/// 系统指令主体文本，不含 PromptBoundary 规则（由 Workflow 在使用侧追加）。
/// </param>
public record PromptTemplate(string SystemInstructions);
