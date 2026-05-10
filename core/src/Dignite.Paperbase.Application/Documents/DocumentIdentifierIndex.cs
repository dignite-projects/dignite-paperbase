using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// Issue #115 L1: <see cref="IDocumentIdentifierIndex"/> 的核心实现。
///
/// <para>
/// 写入路径（<see cref="RegisterAsync"/>）：先 trim + 校验 → <see cref="IDocumentIdentifierRepository.ExistsAsync"/>
/// 幂等检查 → 命中则不插入；未命中则用 <see cref="IGuidGenerator"/> 创建实体并插入。
/// 唯一索引（<c>(TenantId, DocumentId, IdentifierType, IdentifierValue)</c>）是数据库层的并发兜底，
/// 但 ExistsAsync 让正常路径不触发唯一约束错误。
/// </para>
///
/// <para>
/// 多租户：实体构造时显式从 <see cref="ICurrentTenant"/> 取 <c>Id</c> 写入 <c>TenantId</c>；
/// 查询路径依赖 ABP <c>IMultiTenant</c> 自动谓词过滤（仓储层默认行为）。
/// </para>
/// </summary>
public class DocumentIdentifierIndex : IDocumentIdentifierIndex, ITransientDependency
{
    private readonly IDocumentIdentifierRepository _repository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public DocumentIdentifierIndex(
        IDocumentIdentifierRepository repository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant)
    {
        _repository = repository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    public virtual async Task RegisterAsync(
        Guid documentId,
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
        {
            throw new BusinessException(PaperbaseErrorCodes.DocumentIdentifierDocumentIdRequired);
        }

        // 幂等输入校验：与实体构造函数保持一致的 trim 语义。
        // 在 ExistsAsync 之前规范化，避免相同语义不同 raw 字符串走出两条路径。
        var normalizedType = NormalizeOrThrow(identifierType, PaperbaseErrorCodes.DocumentIdentifierTypeRequired);
        var normalizedValue = NormalizeOrThrow(identifierValue, PaperbaseErrorCodes.DocumentIdentifierValueRequired);

        var alreadyExists = await _repository.ExistsAsync(
            documentId,
            normalizedType,
            normalizedValue,
            cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var entity = new DocumentIdentifier(
            _guidGenerator.Create(),
            _currentTenant.Id,
            documentId,
            normalizedType,
            normalizedValue);

        await _repository.InsertAsync(entity, autoSave: true, cancellationToken);
    }

    public virtual async Task<List<Guid>> FindDocumentsAsync(
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = NormalizeOrThrow(identifierType, PaperbaseErrorCodes.DocumentIdentifierTypeRequired);
        var normalizedValue = NormalizeOrThrow(identifierValue, PaperbaseErrorCodes.DocumentIdentifierValueRequired);

        return await _repository.FindDocumentIdsAsync(normalizedType, normalizedValue, cancellationToken);
    }

    public virtual async Task RemoveByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
        {
            throw new BusinessException(PaperbaseErrorCodes.DocumentIdentifierDocumentIdRequired);
        }

        await _repository.RemoveByDocumentIdAsync(documentId, cancellationToken);
    }

    private static string NormalizeOrThrow(string raw, string requiredErrorCode)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new BusinessException(requiredErrorCode);
        }

        return raw.Trim();
    }
}
