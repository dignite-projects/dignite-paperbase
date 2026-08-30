using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Volo.Abp.Modularity;

namespace Dignite.Vault.Extract;

[DependsOn(
    typeof(VaultExtractApplicationModule),
    typeof(VaultExtractDomainTestModule)
    )]
public class VaultExtractApplicationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // IFlexFieldQueryExecutor<Document> is implemented in the EntityFrameworkCore layer, which this
        // stack deliberately does not load — the Application tests run on in-memory fakes, the same reason
        // VaultExtractDomainTestModule supplies PipelineRunRepositoryFake. Without a registration here
        // every DocumentAppService activation fails, whether or not the test filters on a field value.
        //
        // Pass-through, not a Substitute: an unconfigured NSubstitute returns null and the caller would
        // then filter on a null queryable, turning "this test does not care about field filtering" into a
        // NullReferenceException far from the cause. Real pushdown behaviour is covered against SQLite in
        // DocumentFlexFieldPipeline_Tests; what the Application tests exercise is everything around it.
        context.Services.AddSingleton<IFlexFieldQueryExecutor<Document>, PassThroughFlexFieldQueryExecutor>();

        // Same reason, other direction: the index manager writes the derived pivot table, which only the
        // EntityFrameworkCore layer has. That the write paths call it at all is asserted against real EF in
        // DocumentFlexFieldPipeline_Tests; here it only has to resolve.
        context.Services.AddSingleton(Substitute.For<IFlexFieldIndexManager<Document>>());

        // The kernel registers FlexFieldValueMigrator<T> as an open generic, and it needs an
        // IBasicRepository<Document> — real persistence, which this stack has none of. Substituting the
        // interface keeps a rename resolvable here; that a rename actually rewrites the bags is asserted
        // against real EF in DocumentReferenceResolution_Tests, which is the only place it can be.
        context.Services.AddSingleton(Substitute.For<IFlexFieldValueMigrator<Document>>());
    }
}

/// <summary>
/// Returns the query untouched. See the registration above for why this is a hand-written fake rather than
/// a mock.
/// </summary>
public class PassThroughFlexFieldQueryExecutor : IFlexFieldQueryExecutor<Document>
{
    public Task<IQueryable<Document>> ApplyFilterAsync(
        IQueryable<Document> query,
        IReadOnlyList<FlexFieldQueryCondition> conditions,
        CancellationToken cancellationToken = default)
        => Task.FromResult(query);
}
