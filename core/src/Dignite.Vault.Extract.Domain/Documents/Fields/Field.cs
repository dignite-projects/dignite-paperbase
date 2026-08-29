using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dignite.Abp.FlexFields;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// A type-bound field definition (field architecture v3, #558/#559) — Vault Extract's implementation of
/// the FlexFields kernel's <see cref="IFlexField"/> contract. Replaces <see cref="FieldDefinition"/>.
/// <para>
/// Unique constraint stays <c>(TenantId, DocumentTypeId, Name)</c>: fields remain bound to a single
/// document type, deliberately <b>not</b> the tenant-wide reusable field library plus per-usage split
/// that the site repository builds on the same kernel (#558 non-goal). With one binding per field,
/// <see cref="IsRequired"/> / <see cref="IsSearchable"/> live here rather than in a separate usage
/// object, and the provider hands them to the kernel per document.
/// </para>
/// <para>
/// Everything <see cref="IFlexField"/> declares is mapped by the kernel's
/// <c>ConfigureFlexField&lt;Field&gt;()</c>. The properties below it — <see cref="DocumentTypeId"/>,
/// <see cref="DisplayOrder"/>, <see cref="IsRequired"/>, <see cref="IsSearchable"/>,
/// <see cref="IsUniqueKey"/> — are Vault Extract's own and Vault Extract maps them, the same way the
/// site repository adds its own <c>GroupName</c> beside the contract's members.
/// </para>
/// </summary>
public class Field : FullAuditedAggregateRoot<Guid>, IFlexField, IMultiTenant
{
    /// <summary>
    /// Deliberately the <b>same</b> pattern <see cref="FieldDefinition"/> enforces, referenced rather
    /// than copied. The FlexFields kernel validates no format on <c>Name</c> at all — its
    /// <c>ConfigureFlexField</c> maps only a max length, leaving the character set to the downstream —
    /// so adopting the kernel does <b>not</b> carry this guard over by itself. It has to be re-declared
    /// here, or v3 would silently drop it.
    /// </summary>
    private static readonly Regex NameRegex = new(
        FieldDefinitionConsts.NamePattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public virtual Guid? TenantId { get; private set; }

    /// <summary>Internal association to the parent document type by immutable id (#207).</summary>
    public virtual Guid DocumentTypeId { get; private set; }

    /// <summary>
    /// Machine contract key: the JSON schema key in LLM prompts, the <c>ExtractedFields</c> dictionary
    /// key, the downstream contract id — and, new in v3, <b>the key this field's value is stored under
    /// in every document's value bag</b>.
    /// <para>
    /// That last role is why renaming is no longer free. Under v2 the value rows keyed on the immutable
    /// <c>FieldDefinitionId</c>, so a rename touched nothing else; under v3 the bag keys on this string,
    /// so a rename must rewrite every document's bag through
    /// <c>IFlexFieldValueMigrator&lt;Document&gt;</c>, in the order that interface documents. The query
    /// index is unaffected either way — its rows key on the field id.
    /// </para>
    /// <para>
    /// Constrained by <see cref="FieldDefinitionConsts.NamePattern"/>, which is a <b>prompt-injection
    /// boundary</b>, not a formatting preference: this value is concatenated raw into the LLM's schema
    /// message, so the allow-list is what keeps newlines, quotes and Markdown control characters out of
    /// prompt context.
    /// </para>
    /// </summary>
    public virtual string Name { get; private set; } = default!;

    /// <summary>Human-readable label. Does <b>not</b> enter LLM prompts.</summary>
    public virtual string DisplayName { get; private set; } = default!;

    /// <summary>
    /// The LLM extraction instruction — what v2 called <c>Prompt</c>, now carried by the contract's
    /// <see cref="IFlexField.Description"/> (#559 resolution 4 rationale: the kernel's own consumers
    /// already treat Description as the field's AI-facing briefing).
    /// <para>
    /// Optional: when null the model infers what to extract from <see cref="Name"/> and the field type
    /// alone. Uncapped, as #447 decided — it is admin-authored configuration and may be long structured
    /// Markdown. The kernel maps it with no length limit, so nothing narrows that here.
    /// </para>
    /// </summary>
    public virtual string? Description { get; private set; }

    /// <summary>
    /// Registration key of the <c>IFieldType</c> this field is bound to — <c>Text</c>, <c>Number</c>,
    /// <c>DateTime</c>, <c>Boolean</c>, <c>Select</c>, the <c>CKEditor</c> bolt-on that carries v2's
    /// <c>LongText</c>, or Vault Extract's own <c>Tags</c>. Replaces v2's <c>FieldDataType</c> enum.
    /// <para>
    /// A stored key, not a class name: renaming a field type orphans every field bound to it.
    /// </para>
    /// </summary>
    public virtual string FieldTypeName { get; private set; } = default!;

    /// <summary>Type-specific configuration, interpreted by the field type (e.g. <c>Select.Options</c>).</summary>
    public virtual FieldConfigurationDictionary Configuration { get; private set; } = new();

    public virtual int DisplayOrder { get; private set; }

    public virtual bool IsRequired { get; private set; }

    /// <summary>
    /// Whether this field's values are decomposed into the query index, and therefore filterable.
    /// <para>
    /// New in v3 (#558). Under v2 every extracted value was indexed unconditionally — there was no
    /// opt-out — so migrated fields default to <c>true</c> to preserve existing behaviour. A field whose
    /// type is not indexable at all (<c>IFieldType.IndexValueType</c> is null, e.g. the CKEditor type
    /// serving long text) yields nothing regardless of this flag; the kernel's
    /// <c>GetSearchableValues</c> short-circuits on the type before consulting the usage.
    /// </para>
    /// </summary>
    public virtual bool IsSearchable { get; private set; }

    /// <summary>
    /// Whether this field participates in the document type's duplicate-detection key (#411). Not part
    /// of <see cref="IFlexField"/> — a Vault Extract concern the kernel has no opinion about.
    /// </summary>
    public virtual bool IsUniqueKey { get; private set; }

    protected Field()
    {
    }

    public Field(
        Guid id,
        Guid? tenantId,
        Guid documentTypeId,
        string name,
        string displayName,
        string fieldTypeName,
        string? description = null,
        FieldConfigurationDictionary? configuration = null,
        int displayOrder = 0,
        bool isRequired = false,
        bool isSearchable = true,
        bool isUniqueKey = false)
        : base(id)
    {
        TenantId = tenantId;
        DocumentTypeId = Check.NotDefaultOrNull<Guid>(documentTypeId, nameof(documentTypeId));
        SetName(name);
        SetDisplayName(displayName);
        SetFieldTypeName(fieldTypeName);
        SetDescription(description);
        Configuration = configuration ?? new FieldConfigurationDictionary();
        DisplayOrder = displayOrder;
        IsRequired = isRequired;
        IsSearchable = isSearchable;
        IsUniqueKey = isUniqueKey;
    }

    /// <summary>
    /// Renames the field. Callers must not use this directly — go through the domain service that also
    /// rewrites the value bags this name keys into, or every document keeps its value under the old key
    /// where nothing can reach it.
    /// </summary>
    public virtual void SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), FieldDefinitionConsts.MaxNameLength);

        if (!NameRegex.IsMatch(name))
        {
            throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.InvalidName)
                .WithData("name", name)
                .WithData("pattern", FieldDefinitionConsts.NamePattern);
        }

        Name = name;
    }

    /// <summary>
    /// DisplayName never reaches an LLM prompt; rejecting control characters is defense-in-depth for UI
    /// rendering and logs. Same rule as <see cref="FieldDefinition"/>, so a migrated value cannot become
    /// unsavable.
    /// </summary>
    public virtual void SetDisplayName(string displayName)
    {
        Check.NotNullOrWhiteSpace(displayName, nameof(displayName), FieldDefinitionConsts.MaxDisplayNameLength);

        if (displayName.Any(char.IsControl))
        {
            throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.InvalidDisplayName)
                .WithData("displayName", displayName);
        }

        DisplayName = displayName;
    }

    public virtual void SetFieldTypeName(string fieldTypeName)
    {
        FieldTypeName = Check.NotNullOrWhiteSpace(
            fieldTypeName, nameof(fieldTypeName), FlexFieldConsts.MaxFieldTypeNameLength);
    }

    /// <summary>Blank collapses to null — "no instruction", so the model infers from name and type alone.</summary>
    public virtual void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    public virtual void SetConfiguration(FieldConfigurationDictionary? configuration)
    {
        Configuration = configuration ?? new FieldConfigurationDictionary();
    }

    public virtual void SetDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
    }

    public virtual void SetIsRequired(bool isRequired)
    {
        IsRequired = isRequired;
    }

    public virtual void SetIsSearchable(bool isSearchable)
    {
        IsSearchable = isSearchable;
    }

    public virtual void SetIsUniqueKey(bool isUniqueKey)
    {
        IsUniqueKey = isUniqueKey;
    }
}
