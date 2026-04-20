using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Domain.Documents;

public interface IDocumentPipelineRunRepository : IRepository<DocumentPipelineRun, Guid>
{
    Task<DocumentPipelineRun?> GetLatestRunAsync(
        Guid documentId,
        string pipelineCode,
        CancellationToken cancellationToken = default);
}
