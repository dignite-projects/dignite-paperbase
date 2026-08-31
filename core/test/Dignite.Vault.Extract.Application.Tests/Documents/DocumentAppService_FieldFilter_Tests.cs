using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Documents.Fields;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Validation;
using Xunit;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Input validation and field-definition resolution behavior for field value filters in
/// <see cref="DocumentAppService.GetListAsync"/> (verifies the refactor that moved validation upward).
/// Reuses mock repositories from <see cref="DocumentAppServiceReviewTestModule"/>. These cases short-
/// circuit before reaching <c>GetQueryableAsync</c>: DTO validation throws
/// <see cref="AbpValidationException"/> loudly, replacing the old silent-empty behavior, and undefined
/// fields throw <see cref="BusinessException"/>. No real DB is needed. Actual field value matching,
/// <c>GetFieldMatchedIdsAsync</c>'s Documents-anchored LINQ, is covered by
/// <c>EfCoreDocumentRepositorySearch_Tests</c> against real EF (SQLite).
/// </summary>
public class DocumentAppService_FieldFilter_Tests
    : VaultExtractApplicationTestBase<DocumentAppServiceReviewTestModule>
{
    private readonly IDocumentAppService _appService;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldRepository _fieldRepository;

    public DocumentAppService_FieldFilter_Tests()
    {
        _appService = GetRequiredService<IDocumentAppService>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _fieldRepository = GetRequiredService<IFieldRepository>();
    }

    [Fact]
    public async Task Requires_document_type_when_field_filters_present()
    {
        // No DocumentTypeCode plus FieldFilters makes GetDocumentListInput.Validate fail, because field
        // values have no deterministic meaning outside a type.
        await Should.ThrowAsync<AbpValidationException>(() => _appService.GetListAsync(new GetDocumentListInput
        {
            FieldFilters = new List<DocumentFieldFilter> { new() { Name = "amount", Value = "100" } }
        }));
    }

    [Fact]
    public async Task Rejects_filter_without_value()
    {
        // Filter has no value at all, neither equality nor range, so DocumentFieldFilter.Validate fails.
        await Should.ThrowAsync<AbpValidationException>(() => _appService.GetListAsync(new GetDocumentListInput
        {
            DocumentTypeCode = "host.contract",
            FieldFilters = new List<DocumentFieldFilter> { new() { Name = "amount" } }
        }));
    }

    [Fact]
    public async Task Rejects_invalid_field_name()
    {
        // Field name contains characters outside the allowlist, so [RegularExpression] fails.
        await Should.ThrowAsync<AbpValidationException>(() => _appService.GetListAsync(new GetDocumentListInput
        {
            DocumentTypeCode = "host.contract",
            FieldFilters = new List<DocumentFieldFilter> { new() { Name = "bad name!", Value = "x" } }
        }));
    }

    [Fact]
    public async Task Rejects_too_many_field_filters()
    {
        var tooMany = Enumerable.Range(0, DocumentConsts.MaxSearchFieldFilters + 1)
            .Select(i => new DocumentFieldFilter { Name = $"f{i}", Value = "x" })
            .ToList();

        // Exceeds MaxSearchFieldFilters, so [MaxLength] fails.
        await Should.ThrowAsync<AbpValidationException>(() => _appService.GetListAsync(new GetDocumentListInput
        {
            DocumentTypeCode = "host.contract",
            FieldFilters = tooMany
        }));
    }

    [Fact]
    public async Task Throws_when_field_not_defined_on_type()
    {
        // #207: AppService first resolves TypeCode to internal DocumentTypeId, then resolves fields by id.
        var typeId = Guid.NewGuid();
        _documentTypeRepository
            .FindByTypeCodeAsync("host.contract", Arg.Any<CancellationToken>())
            .Returns(new DocumentType(typeId, null, "host.contract", "Contract"));
        _fieldRepository
            .FindByNameAsync(typeId, "ghost", Arg.Any<CancellationToken>())
            .Returns((Field?)null);

        // DTO validation passes, but this type has no "ghost" field, so fail loudly with
        // UnknownExtractedField, a correctable error, instead of silently returning empty.
        var ex = await Should.ThrowAsync<BusinessException>(() => _appService.GetListAsync(new GetDocumentListInput
        {
            DocumentTypeCode = "host.contract",
            FieldFilters = new List<DocumentFieldFilter> { new() { Name = "ghost", Value = "x" } }
        }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.ExtractedField.Unknown);
    }
}
