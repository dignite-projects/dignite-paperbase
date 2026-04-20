using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Documents;

public class EfCoreDocumentPipelineRunRepository
    : EfCoreRepository<PaperbaseDbContext, DocumentPipelineRun, Guid>, IDocumentPipelineRunRepository
{
    public EfCoreDocumentPipelineRunRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<DocumentPipelineRun?> GetLatestRunAsync(
        Guid documentId,
        string pipelineCode,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(r => r.DocumentId == documentId && r.PipelineCode == pipelineCode)
            .OrderByDescending(r => r.AttemptNumber)
            .FirstOrDefaultAsync(GetCancellationToken(cancellationToken));
    }
}
