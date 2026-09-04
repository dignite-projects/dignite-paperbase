namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Format strings for rendering typed field-value columns.
/// <para>
/// <see cref="Date"/> and <see cref="DateTime"/> are <b>serialized contract</b>, not presentation: they are the
/// canonical shapes <c>DocumentFieldValue</c> (v2, removed #593) required on the way in and
/// <c>FlexFieldValueReader</c> / <c>DocumentFieldQueryResolver</c> require now, that the REST / MCP
/// <c>ExtractedFields</c> dictionary emits on the way out, and that the #411 field fingerprint hashes. Changing
/// either value is a wire break and a silent re-hash of every stored fingerprint. They are <c>const</c> so a host
/// cannot widen them.
/// </para>
/// </summary>
public static class FieldValueFormats
{
    /// <summary>Canonical date shape, in and out. Frozen wire contract.</summary>
    public const string Date = "yyyy-MM-dd";

    /// <summary>Canonical offset-free date-time shape, in and out. Frozen wire contract.</summary>
    public const string DateTime = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>
    /// Canonical month shape, in and out — the third <c>DateTime.InputMode</c>. A month field is a date
    /// field whose day carries no information: the value is stored as the first of the month at midnight
    /// (so it stays an ordinary <c>DateTime</c> that sorts, ranges and indexes like any other), and only
    /// the year and month are ever emitted. Frozen wire contract, like the two above.
    /// </summary>
    public const string Month = "yyyy-MM";

    /// <summary>
    /// Minimal shape for a Number in an exported cell: integer 1000 -> "1000", decimal 10.50 -> "10.5", without
    /// the six trailing zeros of <c>decimal(38,6)</c>. Presentation only — deliberately <b>not</b> the fingerprint's
    /// number format, which keeps full precision so two values that differ beyond six decimals do not collide.
    /// </summary>
    public const string CellNumber = "0.######";

    /// <summary>
    /// The fingerprint's number shape (#411): full precision, so two amounts that differ beyond six
    /// decimals never collide. Deliberately not <see cref="CellNumber"/>, which rounds for presentation.
    /// </summary>
    /// <remarks>
    /// Frozen: the fingerprint is a stored hash compared by string equality, so changing this splits the
    /// corpus into documents hashed under the old rule and the new one, which can never match. It lives
    /// here, hoisted out of <c>FlexFieldFingerprintCalculator</c>, because #501 already recorded what
    /// happens when a fingerprint literal is copied per call site instead of hoisted.
    /// </remarks>
    public const string FingerprintNumber = "0.############################";

    /// <summary>
    /// Separator between the values of one multi-valued field inside the fingerprint's canonical string
    /// (ASCII unit separator, U+001F). Frozen, and written as an escape rather than a literal control
    /// character so no editor or transform can silently rewrite it.
    /// </summary>
    public const char FingerprintValueSeparator = '\u001F';

    /// <summary>
    /// Separator between fields inside the fingerprint's canonical string (ASCII record separator,
    /// U+001E). Chosen because neither separator can appear in a normalized value, so distinct field and
    /// value boundaries can never alias into the same canonical string. Frozen; see
    /// <see cref="FingerprintValueSeparator"/> on the escape form.
    /// </summary>
    public const char FingerprintFieldSeparator = '\u001E';
}
