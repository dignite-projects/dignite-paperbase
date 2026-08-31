using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.FlexFields;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;

namespace Dignite.Vault.Extract.Documents.DocumentTypes.Packs;

/// <summary>
/// Config import/export "pack" engine (#444). Serializes a <see cref="DocumentType"/> + its
/// <see cref="Field"/>s to a portable declarative pack and applies a pack back idempotently.
/// <para>
/// Everything goes through the domain entities + managers (never a raw DbContext), so every invariant holds:
/// code/name layer-uniqueness (<see cref="DocumentTypeManager"/> / <see cref="FieldDefinitionManager"/>),
/// entity validation (name pattern, lengths), and the data-safety guard that forbids changing a field's
/// type once extracted values exist.
/// </para>
/// <para>
/// Layer-aware: reads and writes only the caller's current layer (Host = <c>TenantId</c> null, tenant = its
/// GUID) via the ambient <c>IMultiTenant</c> filter + <c>CurrentTenant.Id</c>; there is no cross-layer
/// mixing. Identity is <c>TypeCode</c> / field <c>Name</c> (#207: a rename = a new type/field). Import is
/// atomic: it runs in the ambient application-service unit of work, and all pack versions are validated
/// before any write, so an unsupported version leaves nothing partially applied.
/// </para>
/// </summary>
public class DocumentTypePackAppService : VaultExtractAppService, IDocumentTypePackAppService
{
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldRepository _fieldDefinitionRepository;
    private readonly IFieldTypeResolver _fieldTypeResolver;
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentTypeManager _documentTypeManager;
    private readonly FieldDefinitionManager _fieldDefinitionManager;
    private readonly FieldSchemaPromptBudgetGuard _schemaPromptBudget;

    /// <summary>See <see cref="FieldDefinitionAppService"/>'s field of the same name — same reason: a searchability flip is a complete reindex, not a per-row patch.</summary>
    private readonly IFlexFieldIndexManager<Document> _indexManager;

    private readonly IVaultExtractFieldTypeRegistry _fieldTypeExtensionRegistry;

    public DocumentTypePackAppService(
        IDocumentTypeRepository documentTypeRepository,
        IFieldRepository fieldDefinitionRepository,
        IFieldTypeResolver fieldTypeResolver,
        IDocumentRepository documentRepository,
        DocumentTypeManager documentTypeManager,
        FieldDefinitionManager fieldDefinitionManager,
        FieldSchemaPromptBudgetGuard schemaPromptBudget,
        IFlexFieldIndexManager<Document> indexManager,
        IVaultExtractFieldTypeRegistry fieldTypeExtensionRegistry)
    {
        _documentTypeRepository = documentTypeRepository;
        _fieldDefinitionRepository = fieldDefinitionRepository;
        _fieldTypeResolver = fieldTypeResolver;
        _documentRepository = documentRepository;
        _documentTypeManager = documentTypeManager;
        _fieldDefinitionManager = fieldDefinitionManager;
        _schemaPromptBudget = schemaPromptBudget;
        _indexManager = indexManager;
        _fieldTypeExtensionRegistry = fieldTypeExtensionRegistry;
    }

    [Authorize(VaultExtractPermissions.DocumentTypes.Default)]
    public virtual async Task<DocumentTypePackDto> ExportAsync(Guid id)
    {
        var type = await _documentTypeRepository.GetAsync(id);
        // Defense in depth on top of the IMultiTenant filter: never export another layer's type.
        if (type.TenantId != CurrentTenant.Id)
        {
            throw new EntityNotFoundException(typeof(DocumentType), id);
        }

        var fields = await _fieldDefinitionRepository.GetListAsync(type.Id);
        return MapToPack(type, fields);
    }

    [Authorize(VaultExtractPermissions.DocumentTypes.Default)]
    public virtual async Task<List<DocumentTypePackDto>> ExportAllAsync()
    {
        // The ambient IMultiTenant filter narrows this to the caller's layer.
        var types = await _documentTypeRepository.GetListAsync();
        var packs = new List<DocumentTypePackDto>(types.Count);
        foreach (var type in types.OrderBy(t => t.Priority).ThenBy(t => t.TypeCode))
        {
            var fields = await _fieldDefinitionRepository.GetListAsync(type.Id);
            packs.Add(MapToPack(type, fields));
        }

        return packs;
    }

    // Import always may create types + fields, so both Create permissions gate entry. The Update permissions
    // are asserted lazily, only on the branches that actually update an existing type / field (ImportPackAsync
    // / ImportFieldsAsync), so a CreateOnly import — or a first-time import of all-new types/fields — never
    // demands Update. This is not an LLM-influenced path, so [Authorize] fires normally.
    [Authorize(VaultExtractPermissions.DocumentTypes.Create)]
    [Authorize(VaultExtractPermissions.FieldDefinitions.Create)]
    public virtual async Task<DocumentTypePackImportResultDto> ImportAsync(ImportDocumentTypePacksInput input)
    {
        // Validate every pack version up front, before touching the store, so an unsupported version can
        // never leave earlier packs partially applied (the production UoW would roll back anyway, but the
        // test harness disables the transaction — pre-validation makes the guarantee unconditional).
        foreach (var pack in input.Packs)
        {
            if (pack.Version < DocumentTypePackConsts.MinSupportedVersion ||
                pack.Version > DocumentTypePackConsts.CurrentVersion)
            {
                throw new BusinessException(VaultExtractErrorCodes.DocumentTypePack.UnsupportedVersion)
                    .WithData("TypeCode", pack.TypeCode)
                    .WithData("Version", pack.Version)
                    .WithData("Supported", DocumentTypePackConsts.CurrentVersion);
            }

            // Upconvert before anything else reads a field, so the budget validation below and the import
            // itself both see one shape. A version-1 pack reaching ImportFieldsAsync unconverted would
            // create every field as the default Text type, silently discarding its declared types.
            if (pack.Version < DocumentTypePackConsts.CurrentVersion)
            {
                foreach (var field in pack.Fields)
                {
                    DocumentTypePackV1Upconverter.Upconvert(field);
                }
            }
        }

        // Validate every projected type schema before touching the store. This is aggregate-wide (not one DTO
        // attribute per field) and simulates repeated packs in request order, preserving the method's unconditional
        // no-partial-write guarantee even in the non-transactional test harness.
        await ValidateSchemaPromptBudgetsAsync(input.Packs, input.Mode);

        var result = new DocumentTypePackImportResultDto();
        var searchabilityChanged = false;
        foreach (var pack in input.Packs)
        {
            var (item, packSearchabilityChanged) = await ImportPackAsync(pack, input.Mode);
            result.Items.Add(item);
            searchabilityChanged |= packSearchabilityChanged;

            switch (item.TypeAction)
            {
                case PackItemAction.Created: result.TypesCreated++; break;
                case PackItemAction.Updated: result.TypesUpdated++; break;
                default: result.TypesSkipped++; break;
            }

            result.FieldsCreated += item.FieldsCreated;
            result.FieldsUpdated += item.FieldsUpdated;
            result.FieldsSkipped += item.FieldsSkipped;
        }

        if (searchabilityChanged)
        {
            // One rebuild for the whole import, not one per field: an import can touch many fields across
            // many types in a single call, and RebuildAsync already walks every document once regardless
            // of how many fields changed.
            await _indexManager.RebuildAsync();
        }

        return result;
    }

    protected virtual async Task ValidateSchemaPromptBudgetsAsync(
        List<DocumentTypePackDto> packs,
        PackImportMode mode)
    {
        var projectedByTypeCode = new Dictionary<string, Dictionary<string, string?>>(
            StringComparer.Ordinal);

        foreach (var pack in packs)
        {
            if (!projectedByTypeCode.TryGetValue(pack.TypeCode, out var projectedFields))
            {
                projectedFields = new Dictionary<string, string?>(StringComparer.Ordinal);
                var existingType = await _documentTypeRepository.FindByTypeCodeAsync(pack.TypeCode);
                if (existingType != null)
                {
                    var existingFields = await _fieldDefinitionRepository.GetListAsync(existingType.Id);
                    foreach (var field in existingFields)
                    {
                        projectedFields[field.Name] = field.Description;
                    }
                }

                projectedByTypeCode[pack.TypeCode] = projectedFields;
            }

            foreach (var field in pack.Fields)
            {
                if (!projectedFields.ContainsKey(field.Name) || mode == PackImportMode.CreateOrUpdate)
                {
                    projectedFields[field.Name] = field.Description;
                }
            }

            _schemaPromptBudget.EnsureCanPersist(pack.TypeCode, projectedFields.Values);
        }
    }

    protected virtual async Task<(DocumentTypePackItemResultDto Item, bool SearchabilityChanged)> ImportPackAsync(
        DocumentTypePackDto pack, PackImportMode mode)
    {
        var item = new DocumentTypePackItemResultDto { TypeCode = pack.TypeCode };

        // Match the type by code within the caller's layer (active rows only). Rename = new type (#207).
        var type = await _documentTypeRepository.FindByTypeCodeAsync(pack.TypeCode);

        if (type == null)
        {
            // Both modes create what is missing. CheckCodeAvailableAsync is soft-delete-aware: if a deleted
            // row occupies the code it loud-fails rather than colliding on restore.
            await _documentTypeManager.CheckCodeAvailableAsync(pack.TypeCode);
            type = new DocumentType(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                pack.TypeCode,
                pack.DisplayName,
                pack.Description,
                pack.ConfidenceThreshold,
                pack.Priority);
            StampProvenance(type, pack.Version);
            await _documentTypeRepository.InsertAsync(type, autoSave: true);
            item.TypeAction = PackItemAction.Created;
        }
        else if (mode == PackImportMode.CreateOrUpdate)
        {
            // Updating an existing type needs the Update permission — asserted here rather than as a blanket
            // method attribute, so a CreateOnly / all-new import never requires it.
            await CheckPolicyAsync(VaultExtractPermissions.DocumentTypes.Update);
            type.Update(pack.TypeCode, pack.DisplayName, pack.Description, pack.ConfidenceThreshold, pack.Priority);
            StampProvenance(type, pack.Version);
            await _documentTypeRepository.UpdateAsync(type, autoSave: true);
            item.TypeAction = PackItemAction.Updated;
        }
        else
        {
            // CreateOnly: leave the existing type's own properties untouched (but still add missing fields).
            item.TypeAction = PackItemAction.Skipped;
        }

        var searchabilityChanged = await ImportFieldsAsync(type.Id, pack.Fields, mode, pack.Version, item);
        return (item, searchabilityChanged);
    }

    /// <summary>Returns whether any existing field's <c>IsSearchable</c> flipped, so the caller can rebuild the index once for the whole import.</summary>
    protected virtual async Task<bool> ImportFieldsAsync(
        Guid documentTypeId,
        List<DocumentTypePackFieldDto> fields,
        PackImportMode mode,
        int version,
        DocumentTypePackItemResultDto item)
    {
        var searchabilityChanged = false;

        foreach (var f in fields)
        {
            var existing = await _fieldDefinitionRepository.FindByNameAsync(documentTypeId, f.Name);

            if (existing == null)
            {
                EnsureFieldTypeRegistered(f.FieldTypeName!);
                await _fieldDefinitionManager.CheckNameAvailableAsync(documentTypeId, f.Name);
                CheckSearchable(f.FieldTypeName!, f.IsSearchable);
                var field = new Field(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    documentTypeId,
                    f.Name,
                    f.DisplayName,
                    f.FieldTypeName!,
                    f.Description,
                    f.Configuration,
                    f.DisplayOrder,
                    f.IsRequired,
                    f.IsSearchable,
                    f.IsUniqueKey);
                StampProvenance(field, version);
                await _fieldDefinitionRepository.InsertAsync(field, autoSave: true);
                item.FieldsCreated++;
                // Never needs a rebuild: a field that did not exist a moment ago cannot have values on any
                // existing document to backfill.
            }
            else if (mode == PackImportMode.CreateOrUpdate)
            {
                // Updating an existing field needs the Update permission — asserted lazily; a CreateOnly or
                // all-new import never reaches here.
                await CheckPolicyAsync(VaultExtractPermissions.FieldDefinitions.Update);
                EnsureFieldTypeRegistered(f.FieldTypeName!);
                await GuardFieldMutationAsync(existing, f);
                CheckSearchable(f.FieldTypeName!, f.IsSearchable);
                var wasSearchable = existing.IsSearchable;
                existing.SetDisplayName(f.DisplayName);
                existing.SetDescription(f.Description);
                existing.SetFieldTypeName(f.FieldTypeName!);
                existing.SetConfiguration(f.Configuration);
                existing.SetDisplayOrder(f.DisplayOrder);
                existing.SetIsRequired(f.IsRequired);
                existing.SetIsSearchable(f.IsSearchable);
                existing.SetIsUniqueKey(f.IsUniqueKey);
                StampProvenance(existing, version);
                await _fieldDefinitionRepository.UpdateAsync(existing, autoSave: true);
                item.FieldsUpdated++;
                searchabilityChanged |= wasSearchable != existing.IsSearchable;
            }
            else
            {
                item.FieldsSkipped++;
            }
        }

        return searchabilityChanged;
    }

    /// <summary>
    /// Mirror of the <see cref="FieldDefinitionAppService"/> data-safety guard: never break already-extracted
    /// values by changing the field type under them. A pack that would do so loud-fails (the whole import
    /// rolls back in the ambient UoW) instead of leaving values nothing can render or index.
    /// <para>
    /// One guard where v2 had two: "one value or many" is a property of the field type in v3 (Tags versus
    /// Text), so narrowing a multi-valued field <b>is</b> a field-type change and is covered here.
    /// </para>
    /// </summary>
    protected virtual async Task GuardFieldMutationAsync(Field existing, DocumentTypePackFieldDto pack)
    {
        if (string.Equals(pack.FieldTypeName, existing.FieldTypeName, StringComparison.Ordinal))
        {
            return;
        }

        if (await _documentRepository.AnyFlexFieldValueAsync(existing, IsIndexable(existing.FieldTypeName)))
        {
            throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.DataTypeChangeNotAllowed)
                .WithData("Name", existing.Name);
        }
    }

    /// <summary>Whether values of this field type reach the query index at all; see <see cref="FieldDefinitionAppService"/>.</summary>
    protected virtual bool IsIndexable(string fieldTypeName)
        => _fieldTypeResolver.GetAll()
            .FirstOrDefault(t => string.Equals(t.Name, fieldTypeName, StringComparison.Ordinal))?.IndexValueType != null;

    /// <summary>Same guard as <see cref="FieldDefinitionAppService.EnsureFieldTypeRegistered"/> — a pack is another write path into the same fields, and owes them the same fail-closed check against Vault Extract's own supported set, not just the kernel's full registry.</summary>
    protected virtual void EnsureFieldTypeRegistered(string fieldTypeName)
    {
        var registered = _fieldTypeResolver.GetAll()
            .Any(t => string.Equals(t.Name, fieldTypeName, StringComparison.Ordinal));

        if (!registered || !_fieldTypeExtensionRegistry.IsSupported(fieldTypeName))
        {
            throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.UnknownFieldType)
                .WithData("FieldTypeName", fieldTypeName);
        }
    }

    /// <summary>Same guard as <see cref="FieldDefinitionAppService.CheckSearchable"/> — a pack is another write path into the same fields, and owes them the same fail-closed check.</summary>
    protected virtual void CheckSearchable(string fieldTypeName, bool isSearchable)
    {
        if (isSearchable && !IsIndexable(fieldTypeName))
        {
            throw new BusinessException(VaultExtractErrorCodes.FieldDefinition.FieldTypeNotSearchable)
                .WithData("FieldTypeName", fieldTypeName);
        }
    }

    // Provenance: mark pack-sourced config in ExtraProperties (config metadata on the type/field aggregate,
    // not the Document truth source). A stable value keeps re-import idempotent (no phantom diffs).
    protected virtual void StampProvenance(IHasExtraProperties entity, int version)
    {
        entity.SetProperty(DocumentTypePackConsts.ProvenanceSourceKey, DocumentTypePackConsts.ProvenanceSourceValue);
        entity.SetProperty(DocumentTypePackConsts.ProvenanceVersionKey, version);
    }

    protected virtual DocumentTypePackDto MapToPack(DocumentType type, List<Field> fields)
    {
        return new DocumentTypePackDto
        {
            Version = DocumentTypePackConsts.CurrentVersion,
            TypeCode = type.TypeCode,
            DisplayName = type.DisplayName,
            Description = type.Description,
            ConfidenceThreshold = type.ConfidenceThreshold,
            Priority = type.Priority,
            Fields = fields
                .OrderBy(f => f.DisplayOrder)
                .ThenBy(f => f.Name)
                .Select(f => new DocumentTypePackFieldDto
                {
                    Name = f.Name,
                    DisplayName = f.DisplayName,
                    Description = f.Description,
                    FieldTypeName = f.FieldTypeName,
                    Configuration = f.Configuration,
                    DisplayOrder = f.DisplayOrder,
                    IsRequired = f.IsRequired,
                    IsSearchable = f.IsSearchable,
                    IsUniqueKey = f.IsUniqueKey
                })
                .ToList()
        };
    }
}
