using System;
using System.Threading;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.Audit;

public class AmbientAiCallContext : IAmbientAiCallContext, ISingletonDependency
{
    private static readonly AsyncLocal<CallScope?> _current = new();

    public string? CurrentPromptKey => _current.Value?.PromptKey;
    public string? CurrentPromptVersion => _current.Value?.PromptVersion;
    public string? EvalSampleId => _current.Value?.EvalSampleId;

    public double? OutputConfidence
    {
        get => _current.Value?.OutputConfidence;
        set { if (_current.Value != null) _current.Value.OutputConfidence = value; }
    }

    public IDisposable Enter(string promptKey, string promptVersion)
    {
        var scope = new CallScope(promptKey, promptVersion);
        _current.Value = scope;
        return scope;
    }

    private sealed class CallScope : IDisposable
    {
        public string PromptKey { get; }
        public string PromptVersion { get; }
        public double? OutputConfidence { get; set; }
        public string? EvalSampleId { get; }

        public CallScope(string promptKey, string promptVersion)
        {
            PromptKey = promptKey;
            PromptVersion = promptVersion;
        }

        public void Dispose() => _current.Value = null;
    }
}
