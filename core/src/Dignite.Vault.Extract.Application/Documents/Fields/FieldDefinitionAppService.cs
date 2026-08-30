using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.DocumentTypes;
using Dignite.Vault.Extract.Documents.Fields.Cleanup;
using Dignite.Vault.Extract.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Entities;

namespace Dignite.Vault.Extract.Documents.Fields;

// Authorization is declared per method (#223): reading field schema (active GetListAsync) is decoupled from schema management.
// Therefore there is no class-level [Authorize]; each method explicitly declares its own permission gate,
// using the same programmatic pattern as DocumentAppService.
public class FieldDefinitionAppService : VaultExtractAppService, IFieldDefinitionAppService
{
    private readonly IFieldRepository _repository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly FieldDefinitionManager _fieldDefinitionManager;
    private readonly IFieldTypeResolver _fieldTypeResolver;

    /// <summary>
    /// A field's <c>Name</c> is the key its values are stored under in every document's bag, so a rename
    /// has to move that key on every document — the one thing <c>RebuildAsync</c> explicitly cannot repair,
    /// because re-deriving reads the bag under the same key it is trying to move.
    /// </summary>
    private readonly IFlexFieldValueMigrator<Document> _valueMigrator;

    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly FieldSchemaPromptBudgetGuard _schemaPromptBudget;

    public FieldDefinitionAppService(
        IFieldRepository repository,
        IDocumentTypeRepository documentTypeRepository,
        IDocumentRepository documentRepository,
        FieldDefinitionManager fieldDefinitionManager,
        IFieldTypeResolver fieldTypeResolver,
        IFlexFieldValueMigrator<Document> valueMigrator,
        IBackgroundJobManager backgroundJobManager,
        FieldSchemaPromptBudgetGuard schemaPromptBudget)
    {
        _repository = repository;
        _documentTypeRepository = documentTypeRepository;
        _documentRepository = documentRepository;
        _fieldDefinitionManager = fieldDefinitionManager;
        _fieldTypeResolver = fieldTypeResolver;
        _valueMigrator = valueMigrator;
        _backgroundJobManager = backgroundJobManager;
        _schemaPromptBudget = schemaPromptBudget;
    }

    public virtual async Task<List<FieldDefinitionDto>> GetListAsync(GetFieldDefinitionListInput input)
    {
        // Current tenant layer only (CLAUDE.md "two layers are mutually exclusive, no mixing").
        // Tenant isolation is enforced by the ABP IMultiTenant global filter.
        // When DocumentTypeId is specified, match exactly one type by immutable Id (#207); missing type naturally returns an empty set.
        // Empty = all field definitions in the current layer, the batch path used by MCP list_document_types and similar callers to fetch once and avoid per-type N+1.
        if (input.OnlyDeleted)
        {
            // Trash view is consumed only by schema management screens, so keep the admin gate (#223).
            await CheckPolicyAsync(VaultExtractPermissions.FieldDefinitions.Default);

            // Trash view: traverse soft-delete filter, take only IsDeleted, ordered by deletion time descending.
            using (DataFilter.Disable<ISoftDelete>())
            {
                var queryable = await _repository.GetQueryableAsync();
                var deletedQuery = queryable.Where(f => f.IsDeleted);
                if (input.DocumentTypeId != null)
                {
                    deletedQuery = deletedQuery.Where(f => f.DocumentTypeId == input.DocumentTypeId);
                }
                var deleted = await AsyncExecuter.ToListAsync(
                    deletedQuery.OrderByDescending(f => f.DeletionTime));
                return ObjectMapper.Map<List<Field>, List<FieldDefinitionDto>>(deleted);
            }
        }

        // Active field schema reads are decoupled from schema management (#223): document operators (Documents.Default) need field definitions
        // to drive dynamic field columns / detail field editing / export column selection; field admins (FieldDefinitions.Default)
        // need to read their own management list. Either is enough: fail-closed OR assertion.
        // Batch queries (DocumentTypeId empty) and type-scoped queries use the same permission gate and do not widen visibility;
        // enumerating per type could already obtain the same set.
        if (!await AuthorizationService.IsGrantedAsync(VaultExtractPermissions.Documents.Default) &&
            !await AuthorizationService.IsGrantedAsync(VaultExtractPermissions.FieldDefinitions.Default))
        {
            throw new AbpAuthorizationException();
        }

        if (input.DocumentTypeId == null)
        {
            // Batch path: query all active fields in the current layer once, with IMultiTenant + ISoftDelete filters still applied.
            // Stable-sort by DocumentTypeId then DisplayOrder; callers group in memory.
            var queryable = await _repository.GetQueryableAsync();
            var all = await AsyncExecuter.ToListAsync(
                queryable
                    .OrderBy(f => f.DocumentTypeId)
                    .ThenBy(f => f.DisplayOrder));
            return ObjectMapper.Map<List<Field>, List<FieldDefinitionDto>>(all);
        }

        var list = await _repository.GetListAsync(input.DocumentTypeId.Value);
        return ObjectMapper.Map<List<Field>, List<FieldDefinitionDto>>(list);
    }

    [Authorize(VaultExtractPermissions.FieldDefinitions.Create)]
    public virtual async Task<FieldDefinitionDto> CreateAsync(CreateFieldDefinitionDto input)
    {
        // Parent type must exist in the current layer (#207 Field.DocumentTypeId FK RESTRICT).
        // IMultiTenant + ISoftDelete filters ensure cross-layer / deleted types return null.
        var type = await _documentTypeRepository.FindAsync(input.DocumentTypeId);
        if (type == null)
        {
            throw new EntityNotFoundException(typeof(DocumentType), input.DocumentTypeId);
        }

        EnsureFieldTypeRegistered(input.FieldTypeName);

        // Soft-delete-aware duplicate check owned by the domain service (#304): the same (TenantId, DocumentTypeId, Name)
        // counts as occupied even when soft-deleted, avoiding conflicts with new records on restore.
        await _fieldDefinitionManager.CheckNameAvailableAsync(input.DocumentTypeId, input.Name);
        await EnsureSchemaPromptBudgetAsync(type, replacingFieldId: null, projectedPrompt: input.Description);

        var entity = new Field(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.DocumentTypeId,
            input.Name,
            input.DisplayName,
            input.FieldTypeName,
            input.Description,
            input.Configuration,
            input.DisplayOrder,
            input.IsRequired,
            input.IsSearchable,
            input.IsUniqueKey);

        await _repository.InsertAsync(entity, autoSave: true);
        return ObjectMapper.Map<Field, FieldDefinitionDto>(entity);
    }

    [Authorize(VaultExtractPermissions.FieldDefinitions.Update)]
    public virtual async Task<FieldDefinitionDto> UpdateAsync(Guid id, UpdateFieldDefinitionDto input)
    {
        var entity = await _repository.GetAsync(id);

        // Cross-layer defense: callers may modify only their own layer.
        if (entity.TenantId != CurrentTenant.Id)
        {
            throw new EntityNotFoundException(typeof(Field), id);
        }

        EnsureFieldTypeRegistered(input.FieldTypeName);

        var oldName = entity.Name;
        var renamed = !string.Equals(input.Name, oldName, StringComparison.Ordinal);
        if (renamed)
        {
            // Rename unlock (#207): run the domain duplicate check only when Name changes. Same layer + same type is unique,
            // including soft-deleted occupancy. The manager resolves the owning TypeCode for the error message only on conflict.
            await _fieldDefinitionManager.CheckNameAvailableAsync(entity.DocumentTypeId, input.Name);
        }

        // #207, carried into v3: changing the field type of a field that already holds values is refused.
        // The values were validated against the old type and stay in the bag untouched, but nothing would
        // render or index them under the new one — the same silent disappearance the v2 typed-column guard
        // existed to prevent, arrived at by a different route.
        //
        // v3 drops the separate multi-value narrowing guard: "one value or many" is a property of the type
        // now (Tags versus Text), so narrowing IS a type change and this one guard covers both.
        var fieldTypeChanged = !string.Equals(input.FieldTypeName, entity.FieldTypeName, StringComparison.Ordinal);
        if (fieldTypeChanged &&
            await _documentRepository.AnyFlexFieldValueAsync(entity, IsIndexable(entity.FieldTypeName)))
        {
            throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.DataTypeChangeNotAllowed)
                .WithData("Name", entity.Name);
        }

        var type = await _documentTypeRepository.GetAsync(entity.DocumentTypeId);
        await EnsureSchemaPromptBudgetAsync(type, entity.Id, input.Description);

        entity.SetName(input.Name);
        entity.SetDisplayName(input.DisplayName);
        entity.SetDescription(input.Description);
        entity.SetFieldTypeName(input.FieldTypeName);
        entity.SetConfiguration(input.Configuration);
        entity.SetDisplayOrder(input.DisplayOrder);
        entity.SetIsRequired(input.IsRequired);
        entity.SetIsSearchable(input.IsSearchable);
        entity.SetIsUniqueKey(input.IsUniqueKey);
        await _repository.UpdateAsync(entity, autoSave: true);

        if (renamed)
        {
            // Order matters, and the kernel is explicit about it: the definition changes first, the bags
            // follow, and nothing may synchronize the index in between — an entity synchronized between the
            // two steps projects nothing for this field and loses the index rows it had. Nothing here
            // reindexes afterwards either, and nothing needs to: index rows key on field id and value,
            // neither of which a rename touches.
            await _valueMigrator.RenameFieldAsync(oldName, input.Name);
        }

        return ObjectMapper.Map<Field, FieldDefinitionDto>(entity);
    }

    /// <summary>
    /// Soft-deletes a field definition and reconciles the document state derived from it (#528).
    /// <para>
    /// Deletion alone used to leave every <see cref="DocumentFieldValidationWarning"/> naming this field in place,
    /// and with it the blocking <see cref="DocumentReviewReasons.FieldValidationWarning"/> bit — parking those
    /// documents out of <c>DocumentReadyEto</c> until an operator resolved them by hand or a re-extraction happened
    /// to run. The cleanup is deferred to <see cref="FieldValidationWarningCleanupJob"/> because the affected set is
    /// bounded only by the type's document count; enqueueing happens inside this UoW, so it cannot run for a delete
    /// that rolled back.
    /// </para>
    /// <para>
    /// The scope line is the Ready gate (<c>ReviewReasonPolicy.Blocking</c>): state that <b>withholds</b> a document
    /// is reconciled here, state that is merely stale is not. Hence the second, conditional job — and hence
    /// <c>MissingRequiredFields</c> (non-blocking) and a stale fingerprint under a narrowed-but-non-empty unique-key
    /// set (an under-detection, not a park) are #537, not this path.
    /// </para>
    /// <para>
    /// The values themselves are deliberately left in their bags, as v2 left its value rows: deletion is soft, and
    /// <see cref="RestoreAsync"/> has to be able to bring the field back to values that are still there. They are
    /// invisible while the field is gone — <c>AssembleExtractedFields</c> only emits keys that resolve to a
    /// definition — which is the same "archived field contributes no column" behaviour #499 pinned for the export.
    /// </para>
    /// </summary>
    [Authorize(VaultExtractPermissions.FieldDefinitions.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity.TenantId != CurrentTenant.Id)
        {
            throw new EntityNotFoundException(typeof(Field), id);
        }

        await _repository.DeleteAsync(entity);

        await _backgroundJobManager.EnqueueAsync(
            new FieldValidationWarningCleanupArgs
            {
                FieldDefinitionId = entity.Id,
                TenantId = entity.TenantId
            });

        if (entity.IsUniqueKey)
        {
            // Always enqueue for a deleted unique-key field; the job re-evaluates the FINAL active schema when it
            // runs and only clears the duplicate basis when no unique-key field remains. Deciding "last key" here
            // is racy: two concurrent deletes could each observe the other key and enqueue nothing, while a restore
            // or a newly-created key before job execution could make an enqueue-time "last key" decision stale.
            await _backgroundJobManager.EnqueueAsync(
                new DuplicateBasisCleanupArgs
                {
                    DocumentTypeId = entity.DocumentTypeId,
                    TenantId = entity.TenantId
                });
        }
    }

    [Authorize(VaultExtractPermissions.FieldDefinitions.Delete)]
    public virtual async Task<FieldDefinitionDto> RestoreAsync(Guid id)
    {
        using (DataFilter.Disable<ISoftDelete>())
        {
            var entity = await _repository.GetAsync(id);
            if (entity.TenantId != CurrentTenant.Id)
            {
                throw new EntityNotFoundException(typeof(Field), id);
            }

            // Already inside Disable<ISoftDelete>, so the parent type TypeCode can be resolved even if soft-deleted for error messages / DTO.
            var parentType = await _documentTypeRepository.FindAsync(entity.DocumentTypeId);
            var documentTypeCode = parentType?.TypeCode;

            // Idempotent: return directly when not deleted.
            if (!entity.IsDeleted)
            {
                return ObjectMapper.Map<Field, FieldDefinitionDto>(entity);
            }

            // Parent type must exist and be active, with strict single-layer matching (consistent with FieldExtractionService).
            // If the parent type is still deleted, use the cascading path in IDocumentTypeAppService.RestoreAsync instead.
            if (parentType == null || parentType.IsDeleted)
            {
                throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.ParentTypeMissing)
                    .WithData("DocumentTypeCode", documentTypeCode ?? string.Empty)
                    .WithData("Name", entity.Name);
            }

            // Active field with the same name conflicts. CreateAsync duplicate checks should already prevent this; the domain
            // service keeps a defensive guard and throws RestoreConflict (#304).
            await _fieldDefinitionManager.CheckRestorableAsync(entity);

            // A deleted field is outside the active extraction schema. Restoring it is therefore another
            // configuration write and must validate the projected active set, especially when siblings were added
            // while this field was in the recycle bin or the host lowered the configured ceiling.
            var queryable = await _repository.GetQueryableAsync();
            var activePrompts = await AsyncExecuter.ToListAsync(
                queryable
                    .Where(f =>
                        f.DocumentTypeId == entity.DocumentTypeId &&
                        f.Id != entity.Id &&
                        !f.IsDeleted)
                    .Select(f => f.Description));
            activePrompts.Add(entity.Description);
            _schemaPromptBudget.EnsureCanPersist(parentType.TypeCode, activePrompts);

            entity.IsDeleted = false;
            entity.DeletionTime = null;
            entity.DeleterId = null;
            await _repository.UpdateAsync(entity);

            return ObjectMapper.Map<Field, FieldDefinitionDto>(entity);
        }
    }

    protected virtual async Task EnsureSchemaPromptBudgetAsync(
        DocumentType type,
        Guid? replacingFieldId,
        string? projectedPrompt)
    {
        var fields = await _repository.GetListAsync(type.Id);
        var projectedPrompts = fields
            .Where(f => f.Id != replacingFieldId)
            .Select(f => f.Description)
            .Append(projectedPrompt);

        _schemaPromptBudget.EnsureCanPersist(type.TypeCode, projectedPrompts);
    }

    /// <summary>
    /// A <c>FieldTypeName</c> is a registration key, not a class name, and it arrives from the wire. An
    /// unregistered one would persist a field that no reader, validator or indexer can act on — every one
    /// of them dispatches on this string — so it is rejected at the boundary rather than discovered later
    /// as values that silently fail to save.
    /// </summary>
    protected virtual void EnsureFieldTypeRegistered(string fieldTypeName)
    {
        if (FindFieldType(fieldTypeName) == null)
        {
            throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.UnknownFieldType)
                .WithData("FieldTypeName", fieldTypeName);
        }
    }

    /// <summary>
    /// Whether values of this field type reach the query index at all. A type with no index value type
    /// (long text) produces no index rows however <c>IsSearchable</c> is set, which is what makes the
    /// index unusable as an "are there values" oracle for it.
    /// </summary>
    protected virtual bool IsIndexable(string fieldTypeName)
        => FindFieldType(fieldTypeName)?.IndexValueType != null;

    /// <summary>
    /// Lookup that tolerates an unknown name. <c>IFieldTypeResolver.Get</c> throws an
    /// <c>AbpException</c> — right for the internal callers that only ever pass a name a stored field
    /// already carries, wrong here, where the name arrives on the wire and "unknown" is a 400 with a
    /// localized message rather than a 500.
    /// </summary>
    protected virtual IFieldType? FindFieldType(string fieldTypeName)
        => _fieldTypeResolver.GetAll().FirstOrDefault(t => string.Equals(t.Name, fieldTypeName, StringComparison.Ordinal));
}
