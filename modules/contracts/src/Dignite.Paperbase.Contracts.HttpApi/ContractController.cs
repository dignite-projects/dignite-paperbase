using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;

namespace Dignite.Paperbase.Contracts;

[Area(PaperbaseContractsRemoteServiceConsts.ModuleName)]
[RemoteService(Name = PaperbaseContractsRemoteServiceConsts.RemoteServiceName)]
[Route("api/paperbase/contracts")]
public class ContractController : PaperbaseContractsController, IContractAppService
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

    [HttpGet]
    [Route("export")]
    public virtual async Task<IRemoteStreamContent> ExportAsync(GetContractListInput input)
    {
        return await _contractAppService.ExportAsync(input);
    }
}
