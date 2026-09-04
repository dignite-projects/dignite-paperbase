using System;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.Documents.Fields.Migration;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents.Fields.Migration;

/// <summary>
/// The surviving half of the v1-dataType -> v3 field-type mapping table (#559 resolution 1). The
/// entity-consuming overloads (<c>Map(FieldDefinition)</c> / <c>MapType(FieldDefinition)</c>) and their
/// coverage were removed with the v2 <c>FieldDefinition</c> entity and its one-shot migrator (#593);
/// <see cref="FieldDefinitionToFieldMapper.MapType(FieldDataType, bool)"/> itself stays live, shared by
/// <c>FieldDraftSuggestionAppService</c> and <c>DocumentTypePackV1Upconverter</c> — see the class doc
/// comment on <see cref="FieldDefinitionToFieldMapper"/>.
/// </summary>
public class FieldDefinitionToFieldMapper_Tests
{
    /// <summary>
    /// A shape v2 could not have produced (its own ValidateMultiValue refused it) must fail loudly rather
    /// than be mapped to a guess.
    /// </summary>
    [Fact]
    public void Refuses_to_guess_for_a_shape_v2_could_not_produce()
    {
        Should.Throw<ArgumentException>(() =>
            FieldDefinitionToFieldMapper.MapType(FieldDataType.Number, allowMultiple: true));
    }
}
