using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Documents;

public interface IDocumentTenantFieldRepository : IRepository<DocumentTenantField, Guid>
{
    Task<List<DocumentTenantField>> GetByDocumentAsync(
        Guid? tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<DocumentTenantField?> FindByDocumentAndNameAsync(
        Guid? tenantId,
        Guid documentId,
        string fieldName,
        CancellationToken cancellationToken = default);
}
