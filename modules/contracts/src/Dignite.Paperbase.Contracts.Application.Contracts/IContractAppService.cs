using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Dignite.Paperbase.Contracts;

public interface IContractAppService : IApplicationService
{
    Task<ContractDto> GetAsync(Guid id);

    Task<PagedResultDto<ContractDto>> GetListAsync(GetContractListInput input);

    Task<ContractDto> UpdateAsync(Guid id, UpdateContractDto input);

    Task ConfirmAsync(Guid id);

    Task<IRemoteStreamContent> ExportAsync(GetContractListInput input);
}
