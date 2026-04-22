using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Contracts;

[Area(ContractsRemoteServiceConsts.ModuleName)]
[RemoteService(Name = ContractsRemoteServiceConsts.RemoteServiceName)]
[Route("api/paperbase/contracts")]
public class ContractController : ContractsController, IContractAppService
{
    private readonly IContractAppService _contractAppService;

    public ContractController(IContractAppService contractAppService)
    {
        _contractAppService = contractAppService;
    }

    [HttpGet]
    [Route("{id}")]
    public virtual async Task<ContractDto> GetAsync(Guid id)
    {
        return await _contractAppService.GetAsync(id);
    }

    [HttpGet]
    public virtual async Task<PagedResultDto<ContractDto>> GetListAsync(GetContractListInput input)
    {
        return await _contractAppService.GetListAsync(input);
    }

    [HttpPut]
    [Route("{id}")]
    public virtual async Task<ContractDto> UpdateAsync(Guid id, UpdateContractDto input)
    {
        return await _contractAppService.UpdateAsync(id, input);
    }

    [HttpPost]
    [Route("{id}/confirm")]
    public virtual async Task ConfirmAsync(Guid id)
    {
        await _contractAppService.ConfirmAsync(id);
    }
}
