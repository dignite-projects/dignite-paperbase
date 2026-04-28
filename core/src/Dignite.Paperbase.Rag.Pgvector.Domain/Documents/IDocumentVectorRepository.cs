using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Rag.Pgvector.Documents;

public interface IDocumentVectorRepository : IRepository<DocumentVector, Guid>
{
    /// <summary>
    /// Delete the document-level vector for the given document.
    /// No-op if no record exists for documentId.
    /// </summary>
    Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
