using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Samples;

public interface ISampleAppService : IApplicationService
{
    Task<SampleDto> GetAsync();

    Task<SampleDto> GetAuthorizedAsync();
}
