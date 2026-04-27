using System.Linq;
using Dignite.Paperbase.Documents;
using Microsoft.EntityFrameworkCore;

namespace Dignite.Paperbase;

public static class PaperbaseEntityFrameworkCoreQueryableExtensions
{
    public static IQueryable<Document> IncludeDetails(
        this IQueryable<Document> queryable,
        bool include = true)
    {
        if (!include)
        {
            return queryable;
        }

        return queryable
            .Include(x => x.PipelineRuns);
    }
}
