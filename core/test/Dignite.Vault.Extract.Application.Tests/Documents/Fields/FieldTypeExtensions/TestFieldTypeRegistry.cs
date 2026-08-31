using Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;
using Dignite.Vault.Extract.FlexFields;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// A ready-made <see cref="IVaultExtractFieldTypeRegistry"/> for pure unit tests that call
/// <c>FlexFieldValueReader</c> / <c>FlexFieldValueSchemaBuilder</c> / <c>FlexFieldValueJsonWriter</c> /
/// <c>ExportCellRenderer</c> / <c>FlexFieldFingerprintCalculator</c> directly, with no ABP module
/// bootstrap. Every extension class is a plain, dependency-free type, so constructing this needs no DI
/// container - the same registry a real host builds by injecting <c>IEnumerable&lt;IVaultExtractFieldTypeExtension&gt;</c>.
/// </summary>
public static class TestFieldTypeRegistry
{
    public static readonly IVaultExtractFieldTypeRegistry Default = new VaultExtractFieldTypeRegistry(new IVaultExtractFieldTypeExtension[]
    {
        new TextFieldTypeExtension(),
        new NumberFieldTypeExtension(),
        new BooleanFieldTypeExtension(),
        new DateTimeFieldTypeExtension(),
        new SelectFieldTypeExtension(),
        new CKEditorFieldTypeExtension(),
        new TagsFieldTypeExtension()
    });
}
