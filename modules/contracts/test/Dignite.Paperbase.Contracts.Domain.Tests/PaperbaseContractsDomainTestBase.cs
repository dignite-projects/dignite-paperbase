using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

/* Inherit from this class for your domain layer tests.
 * See SampleManager_Tests for example.
 */
public abstract class PaperbaseContractsDomainTestBase<TStartupModule> : PaperbaseContractsTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
