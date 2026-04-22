using Dignite.Paperbase.Contracts.Contracts;
using Dignite.Paperbase.Contracts.Dtos;
using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;

namespace Dignite.Paperbase.Contracts;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ContractToContractDtoMapper : MapperBase<Contract, ContractDto>
{
    public override partial ContractDto Map(Contract source);

    public override partial void Map(Contract source, ContractDto destination);
}
