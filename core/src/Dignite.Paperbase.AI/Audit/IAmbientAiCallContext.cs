using System;

namespace Dignite.Paperbase.AI.Audit;

/// <summary>
/// 当前 AI 调用的环境上下文。
/// 通过 Enter() 声明本次使用哪条提示词，AuditedChatClient 自动写入 Metadata。
/// </summary>
public interface IAmbientAiCallContext
{
    string? CurrentPromptKey { get; }
    string? CurrentPromptVersion { get; }
    double? OutputConfidence { get; set; }
    string? EvalSampleId { get; }

    IDisposable Enter(string promptKey, string promptVersion);
}
