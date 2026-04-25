using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Domain.Documents;

public interface IDocumentRepository : IRepository<Document, Guid>
{
    Task<Document?> FindByBlobNameAsync(
        string blobName,
        CancellationToken cancellationToken = default);

    Task<List<Document>> GetListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
