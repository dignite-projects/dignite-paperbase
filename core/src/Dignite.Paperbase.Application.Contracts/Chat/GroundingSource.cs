namespace Dignite.Paperbase.Chat;

/// <summary>
/// Categorizes which kinds of tools the model invoked in a single chat turn.
/// Lets the project distinguish "no grounding at all" from "grounded via vector search"
/// from "grounded via structured business tools" — not just a binary "did search run?".
/// </summary>
/// <remarks>
/// Classification rule (Issue #98):
/// <list type="bullet">
///   <item><c>search_paperbase_documents</c> → <see cref="Vector"/></item>
///   <item>everything else (business contributor tools: search_contracts, get_contract_detail, ...) → <see cref="Structured"/></item>
/// </list>
/// Both classes invoked in the same turn → <see cref="Mixed"/>.
/// </remarks>
public enum GroundingSource
{
    /// <summary>No tool was invoked. The model answered from prior context only (degraded).</summary>
    None = 0,

    /// <summary>Only vector search was invoked.</summary>
    Vector = 1,

    /// <summary>Only structured business tools were invoked (no vector search).</summary>
    Structured = 2,

    /// <summary>Both vector search and structured business tools were invoked.</summary>
    Mixed = 3
}
