using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

/// <summary>
/// Fail-closed guards of <see cref="DocumentFieldQueryResolver"/> against the real database (#606
/// test-coverage gap), ported from v2's <c>EfCoreDocumentRepositorySearch_Tests</c>: a range against an
/// unordered field type, a value that does not parse as its declared type, a completely empty filter, and
/// an offset-bearing DateTime value must all loud-fail rather than silently returning no rows or a
/// mis-parsed comparison.
/// <para>
/// Follows the same real-repository pattern <c>DocumentExportAppService_Filter_Tests</c> already uses to
/// call <see cref="DocumentFieldQueryResolver.ResolveAsync"/> directly: the resolver needs a genuine
/// <see cref="IFieldRepository"/> and <see cref="IFieldTypeResolver"/> to look up a field's declared value
/// type, which a hand-built mock would just be re-asserting rather than proving.
/// </para>
/// </summary>
public class DocumentFieldQueryResolver_Tests : VaultExtractEntityFrameworkCoreTestBase
{
    private const string TypeCode = "contract.general";

    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldRepository _fieldRepository;
    private readonly IFieldTypeResolver _fieldTypeResolver;
    private readonly IGuidGenerator _guidGenerator;

    public DocumentFieldQueryResolver_Tests()
    {
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _fieldRepository = GetRequiredService<IFieldRepository>();
        _fieldTypeResolver = GetRequiredService<IFieldTypeResolver>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Range_on_string_field_throws()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = await SeedTypeAsync();
            await SeedFieldAsync(typeId, "party", TextFieldType.ControlName);

            var ex = await Should.ThrowAsync<BusinessException>(() => DocumentFieldQueryResolver.ResolveAsync(
                _fieldRepository, _fieldTypeResolver,
                new List<DocumentFieldFilter> { new() { Name = "party", Min = "a", Max = "z" } },
                typeId, TypeCode));

            ex.Code.ShouldBe(VaultExtractErrorCodes.ExtractedField.FieldTypeDoesNotSupportRange);
        });
    }

    /// <summary>
    /// Boolean's <c>IndexValueType</c> is its own <see cref="FlexFieldValueType.Boolean"/> slot, not
    /// String — the guard still rejects the range because Boolean is not one of the two ordered value
    /// types, not because it happens to share String's slot.
    /// </summary>
    [Fact]
    public async Task Range_on_boolean_field_throws()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = await SeedTypeAsync();
            await SeedFieldAsync(typeId, "active", Dignite.Abp.FlexFields.Boolean.BooleanFieldType.ControlName);

            var ex = await Should.ThrowAsync<BusinessException>(() => DocumentFieldQueryResolver.ResolveAsync(
                _fieldRepository, _fieldTypeResolver,
                new List<DocumentFieldFilter> { new() { Name = "active", Min = "false", Max = "true" } },
                typeId, TypeCode));

            ex.Code.ShouldBe(VaultExtractErrorCodes.ExtractedField.FieldTypeDoesNotSupportRange);
        });
    }

    [Fact]
    public async Task Value_not_matching_declared_type_throws()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = await SeedTypeAsync();
            await SeedFieldAsync(typeId, "count", NumberFieldType.ControlName);

            // "abc" cannot parse as the declared Number type -> loud fail, not silent empty.
            var ex = await Should.ThrowAsync<BusinessException>(() => DocumentFieldQueryResolver.ResolveAsync(
                _fieldRepository, _fieldTypeResolver,
                new List<DocumentFieldFilter> { new() { Name = "count", Value = "abc" } },
                typeId, TypeCode));

            ex.Code.ShouldBe(VaultExtractErrorCodes.ExtractedField.InvalidValue);
        });
    }

    [Fact]
    public async Task Field_query_with_no_value_throws_fail_closed()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = await SeedTypeAsync();
            await SeedFieldAsync(typeId, "count", NumberFieldType.ControlName);

            // Empty equality/range filter is incomplete and must loud fail. DocumentFieldFilter.Validate
            // already rejects this at the DTO layer; this proves the resolver-level defense in depth too.
            var ex = await Should.ThrowAsync<BusinessException>(() => DocumentFieldQueryResolver.ResolveAsync(
                _fieldRepository, _fieldTypeResolver,
                new List<DocumentFieldFilter> { new() { Name = "count" } },
                typeId, TypeCode));

            ex.Code.ShouldBe(VaultExtractErrorCodes.ExtractedField.InvalidValue);
        });
    }

    [Theory]
    [InlineData("2024-01-01T10:00:00+08:00")] // Explicit offset.
    [InlineData("2024-01-01T10:00:00Z")] // UTC 'Z'.
    public async Task DateTime_offset_bearing_value_throws(string offsetInput)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = await SeedTypeAsync();
            await SeedFieldAsync(typeId, "created", Dignite.Abp.FlexFields.Date.DateTimeFieldType.ControlName);

            // DateTime input carrying a timezone conflicts with storage-side wall-clock semantics -> treat
            // as dirty input and loud fail, not silent empty.
            var ex = await Should.ThrowAsync<BusinessException>(() => DocumentFieldQueryResolver.ResolveAsync(
                _fieldRepository, _fieldTypeResolver,
                new List<DocumentFieldFilter> { new() { Name = "created", Value = offsetInput } },
                typeId, TypeCode));

            ex.Code.ShouldBe(VaultExtractErrorCodes.ExtractedField.InvalidValue);
        });
    }

    // --- helpers ---

    private async Task<Guid> SeedTypeAsync()
    {
        var id = _guidGenerator.Create();
        await _documentTypeRepository.InsertAsync(
            new DocumentType(id, null, TypeCode, TypeCode), autoSave: true);
        return id;
    }

    private async Task SeedFieldAsync(Guid documentTypeId, string name, string fieldTypeName)
    {
        await _fieldRepository.InsertAsync(
            new Field(
                _guidGenerator.Create(), null, documentTypeId,
                name: name, displayName: name, fieldTypeName: fieldTypeName),
            autoSave: true);
    }
}
