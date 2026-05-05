using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Services;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Contracts.Contracts;

public class ContractManager : DomainService
{
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public ContractManager(
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant)
    {
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    public virtual Task<Contract> CreateAsync(
        Guid documentId,
        string documentTypeCode,
        ContractFields fields)
    {
        return Task.FromResult(new Contract(
            _guidGenerator.Create(),
            _currentTenant.Id,
            documentId,
            documentTypeCode,
            fields));
    }
}
