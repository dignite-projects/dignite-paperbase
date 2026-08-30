using Dignite.Abp.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Output of "draft from prompt" (issue #264): AI-drafted field metadata <b>draft</b>.
/// <para>
/// This is a <b>one-time draft, not continuous truth derivation</b>. After fields land in frontend
/// form controls, users can still review / modify every item before saving. <c>Name</c> is populated
/// only when <see cref="DraftFieldDefinitionInput.ForNewField"/> is true, already sanitized as a
/// whitelist slug. When editing an existing field it is always an empty string (guardrail 1:
/// contract-level identity key is frozen and not overwritten by AI).
/// </para>
/// <para>
/// Any field may be <b>empty / default</b>: when the LLM is unavailable, times out, or returns
/// non-JSON, the whole result falls back to a conservative draft (empty DisplayName + empty Name +
/// the default Text field type + all false). The frontend treats empty DisplayName as "drafting
/// unavailable", preserves user-entered content, and prompts manual input.
/// </para>
/// </summary>
public class FieldDefinitionDraftDto
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Populated only when input <see cref="DraftFieldDefinitionInput.ForNewField"/>=true; always empty for edits.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Registration key of the drafted field type. The LLM never sees the live field-type registry — it
    /// picks from a compile-time allow-list of coarse kinds, which the server maps to a field type plus
    /// configuration exactly as the v2-to-v3 migration does. Building the model's vocabulary from the
    /// registry at runtime would be a runtime-constructed instruction, which the security conventions
    /// forbid outright.
    /// </summary>
    public string FieldTypeName { get; set; } = DefaultFieldTypeName;

    /// <summary>Type-specific configuration that goes with <see cref="FieldTypeName"/>, e.g. the DateTime input mode.</summary>
    public FieldConfigurationDictionary Configuration { get; set; } = new();

    /// <summary>Guardrail 3: document semantics do not signal whether a field is required, so AI only returns conservative default false and admins decide.</summary>
    public bool IsRequired { get; set; }

    // The kind a draft falls back to, matching the shape the whole DTO falls back to when drafting fails.
    // A literal for the same reason CreateFieldDefinitionDto uses one: Application.Contracts does not depend
    // on the field-type implementations to name a default.
    private const string DefaultFieldTypeName = "Text";
}
