using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Domain.Documents;

public interface IDocumentRelationRepository : IRepository<DocumentRelation, Guid>
{
    Task<List<DocumentRelation>> GetListByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
