using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

/* Inherit from this class for your application layer tests.
 * See SampleAppService_Tests for example.
 */
public abstract class PaperbaseApplicationTestBase<TStartupModule> : PaperbaseTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
