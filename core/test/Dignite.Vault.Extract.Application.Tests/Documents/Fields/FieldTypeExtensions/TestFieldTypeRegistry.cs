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
/// <para>
/// <c>Table</c> is the one extension here that is not fully dependency-free: it recurses into its own
/// columns' extensions through a registry reference, resolved lazily via DI in a real host (see
/// <see cref="TableFieldTypeExtension.Registry"/>'s own doc for why). Built in two steps here instead -
/// construct the extension, build the registry the same way as every other type, then hand the finished
/// registry back to the one extension that needs it.
/// </para>
/// </summary>
public static class TestFieldTypeRegistry
{
    public static readonly IVaultExtractFieldTypeRegistry Default = Build();

    private static IVaultExtractFieldTypeRegistry Build()
    {
        var table = new TableFieldTypeExtension();
        var registry = new VaultExtractFieldTypeRegistry(new IVaultExtractFieldTypeExtension[]
        {
            new TextFieldTypeExtension(),
            new NumberFieldTypeExtension(),
            new BooleanFieldTypeExtension(),
            new DateTimeFieldTypeExtension(),
            new SelectFieldTypeExtension(),
            new CKEditorFieldTypeExtension(),
            new TagsFieldTypeExtension(),
            table
        });
        table.Registry = registry;
        return registry;
    }
}
