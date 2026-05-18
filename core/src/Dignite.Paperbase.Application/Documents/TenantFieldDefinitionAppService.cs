using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace Dignite.Paperbase.Documents;

[Authorize(PaperbasePermissions.Documents.ConfirmClassification)]
public class TenantFieldDefinitionAppService : PaperbaseAppService, ITenantFieldDefinitionAppService
{
    private readonly ITenantFieldDefinitionRepository _repository;

    public TenantFieldDefinitionAppService(ITenantFieldDefinitionRepository repository)
    {
        _repository = repository;
    }

    public virtual async Task<List<TenantFieldDefinitionDto>> GetByDocumentTypeAsync(string documentTypeCode)
    {
        var list = await _repository.GetByDocumentTypeAsync(CurrentTenant.Id, documentTypeCode);
        return list.Select(MapToDto).ToList();
    }

    public virtual async Task<TenantFieldDefinitionDto> CreateAsync(CreateTenantFieldDefinitionDto input)
    {
        // 显式 TenantId 谓词避免 ambient filter 绕过
        var existing = await _repository.FindByNameAsync(CurrentTenant.Id, input.DocumentTypeCode, input.Name);
        if (existing != null)
        {
            throw new BusinessException("Paperbase:TenantFieldDefinitionAlreadyExists")
                .WithData("DocumentTypeCode", input.DocumentTypeCode)
                .WithData("Name", input.Name);
        }

        var entity = new TenantFieldDefinition(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.DocumentTypeCode,
            input.Name,
            input.Prompt,
            input.DataType,
            input.DisplayOrder,
            input.IsRequired);

        await _repository.InsertAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    public virtual async Task<TenantFieldDefinitionDto> UpdateAsync(Guid id, UpdateTenantFieldDefinitionDto input)
    {
        var entity = await _repository.GetAsync(id);

        // 跨租户防御——即使 ambient DataFilter 被 disable
        if (entity.TenantId != CurrentTenant.Id)
        {
            throw new EntityNotFoundException(typeof(TenantFieldDefinition), id);
        }

        entity.Update(input.Prompt, input.DataType, input.DisplayOrder, input.IsRequired);
        await _repository.UpdateAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity.TenantId != CurrentTenant.Id)
        {
            throw new EntityNotFoundException(typeof(TenantFieldDefinition), id);
        }
        await _repository.DeleteAsync(entity);
    }

    private static TenantFieldDefinitionDto MapToDto(TenantFieldDefinition entity) => new()
    {
        Id = entity.Id,
        TenantId = entity.TenantId,
        DocumentTypeCode = entity.DocumentTypeCode,
        Name = entity.Name,
        Prompt = entity.Prompt,
        DataType = entity.DataType,
        DisplayOrder = entity.DisplayOrder,
        IsRequired = entity.IsRequired
    };
}
