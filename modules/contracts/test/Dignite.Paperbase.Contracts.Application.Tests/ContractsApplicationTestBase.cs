using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

/* Inherit from this class for your application layer tests.
 * See SampleAppService_Tests for example.
 */
public abstract class ContractsApplicationTestBase<TStartupModule> : ContractsTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
